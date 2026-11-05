"""Read-only evidence collection; normalization, matching and accounting belong to the dashboard.

No default date range, scheduler, copied browser credentials, account mutations or accounting calls.
Receipts can be attached only to a dashboard-selected transaction (manifest); the collector never matches jobs.
"""
import argparse
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import sys
import time
from urllib.parse import urlparse

MAX_BYTES = 20 * 1024 * 1024


def private_directory(path):
    path = Path(path).resolve()
    source_root = Path(__file__).resolve().parents[2]
    if (source_root / "CharleyCompany.Dashboard.Web.csproj").exists() and (path == source_root or source_root in path.parents):
        raise RuntimeError("Private runtime paths must be outside the source checkout")
    path.mkdir(parents=True, exist_ok=True, mode=0o700)
    if os.name != "nt":
        path.chmod(0o700)
    return path


def atomic(path, data):
    temporary = path.with_name(path.name + ".part")
    with open(temporary, "wb") as stream:
        if os.name != "nt":
            os.chmod(temporary, 0o600)
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


@contextlib.contextmanager
def single_run(directory):
    # Crash-safe OS lock; do not leave a stale PID lock after interruptions.
    with open(directory / "collector.lock", "a+b") as stream:
        if os.name == "nt":
            import msvcrt
            stream.seek(0)
            if not stream.read(1):
                stream.write(b"0")
                stream.flush()
            stream.seek(0)
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            yield
        finally:
            if os.name == "nt":
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)


def submit(config, db, path, kind="Csv", purchase_id=""):
    import requests
    path = Path(path)
    if not path.is_file() or path.stat().st_size > MAX_BYTES:
        raise RuntimeError("Original is missing or exceeds the upload limit")
    data = path.read_bytes()
    if kind != "Csv" and not data.startswith(b"%PDF-"):
        raise RuntimeError("PDF handoff requires an original PDF")
    digest = hashlib.sha256(data).hexdigest()
    receipt_key = ":".join((config["dashboard_url"], kind, purchase_id, digest))
    if db.execute("select 1 from receipts where receipt_key=?", (receipt_key,)).fetchone():
        return False
    endpoint = config["dashboard_url"].rstrip("/")
    parsed = urlparse(endpoint)
    if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise RuntimeError("An HTTPS dashboard origin without embedded credentials is required")
    key = Path(config["ingestion_key_file"]).read_text(encoding="utf-8").strip()
    if not key:
        raise RuntimeError("Ingestion credential is not configured")
    response = requests.post(endpoint + "/api/home-depot-history/intake",
                             headers={"X-CCV-HomeDepot-Key": key},
                             files={"document": ("original.csv" if kind == "Csv" else "original.pdf", data,
                                                  "text/csv" if kind == "Csv" else "application/pdf")},
                             data={"kind": kind, "purchaseId": purchase_id}, timeout=(15, 180), allow_redirects=False)
    if response.status_code != 200:
        # Never print error bodies, private source content, credential headers or signed URLs.
        raise RuntimeError("Dashboard intake failed with HTTP " + str(response.status_code))
    receipt = response.json()
    if not isinstance(receipt.get("documentId"), str) or not receipt["documentId"]:
        raise RuntimeError("Dashboard did not acknowledge a durable document")
    db.execute("insert into receipts values (?, ?, ?)", (receipt_key, receipt["documentId"], time.time()))
    db.commit()
    return True


def collect(config, state, inbox, use_current_range, login=False):
    """Observed purchase-history UI; unobserved download selectors require protected configuration.

    Only GET/HEAD/OPTIONS browser requests are allowed. Unknown POST reads fail closed until
    independently established as read-only, rather than allowing arbitrary portal API writes.
    """
    from playwright.sync_api import sync_playwright
    portal = config["portal"]
    profile = private_directory(portal["profile_directory"])
    date = portal.get("date_range", {})
    if not login and not use_current_range and not (date.get("start") and date.get("end")):
        raise RuntimeError("Supply an explicit date range or --use-current-range; no historical start is assumed")
    if date.get("start") and date.get("end") and date["start"] > date["end"]:
        raise RuntimeError("Date range is reversed")
    blocked = []
    checkpoint = {"status": "Partial", "completed_pages": 0, "csv": "Unavailable", "receipts": "Unavailable",
                  "returns": "Unavailable in CSV export", "cancellations": "Unavailable in CSV export",
                  "payment_status": "Not established", "configured_range": date, "resume": "Replay range with acknowledged-document deduplication"}
    def save():
        atomic(state / "collection.json", json.dumps(checkpoint).encode())
    save()
    with sync_playwright() as playwright:
        kwargs = {"headless": False if login else portal.get("headless", True), "accept_downloads": True}
        if portal.get("browser_executable_path"):
            kwargs["executable_path"] = portal["browser_executable_path"]
        browser = playwright.chromium.launch_persistent_context(str(profile), **kwargs)
        try:
            if login:
                page = browser.pages[0] if browser.pages else browser.new_page()
                page.goto(portal["url"])
                # Owner interacts directly. No password/MFA/CAPTCHA collection or logging.
                input("Complete Home Depot sign-in in the dedicated browser, then press Enter here. ")
                return
            def route(request_route):
                request = request_route.request
                host = urlparse(request.url).hostname or ""
                if request.method not in ("GET", "HEAD", "OPTIONS"):
                    blocked.append("Unsupported non-GET request")
                    request_route.abort()
                elif request.is_navigation_request() and not (host == "homedepot.com" or host.endswith(".homedepot.com")):
                    blocked.append("Unexpected navigation host")
                    request_route.abort()
                else:
                    request_route.continue_()
            browser.route("**/*", route)
            page = browser.pages[0] if browser.pages else browser.new_page()
            page.goto(portal["url"], wait_until="domcontentloaded")
            if page.get_by_role("button", name="Export", exact=True).count() != 1:
                checkpoint["status"] = "AuthenticationOrLayoutBlocked"
                save()
                raise RuntimeError("Owner sign-in/MFA/CAPTCHA or unsupported portal layout blocks collection")
            if date.get("start") and date.get("end"):
                for setting in ("start_selector", "end_selector", "apply_selector"):
                    if not date.get(setting):
                        raise RuntimeError("Date-filter controls require observed protected selector configuration")
                page.locator(date["start_selector"]).fill(date["start"])
                page.locator(date["end_selector"]).fill(date["end"])
                page.locator(date["apply_selector"]).click()
            checkpoint["visible_range"] = page.get_by_role("button", name="Date Placed", exact=True).inner_text()
            # Export the selected range. No claim is made that CSV is complete evidence.
            dialog_selector = portal.get("export_dialog_selector")
            download_selector = portal.get("export_button_selector")
            if dialog_selector and download_selector:
                page.get_by_role("button", name="Export", exact=True).click()
                dialog = page.locator(dialog_selector)
                dialog.get_by_text(portal.get("details_label", "Details Data"), exact=True).click()
                with page.expect_download() as pending:
                    dialog.locator(download_selector).click()
                download = pending.value
                temporary = inbox / "portal-export.part"
                download.save_as(str(temporary))
                if temporary.stat().st_size > MAX_BYTES:
                    raise RuntimeError("Export exceeds dashboard upload limit")
                data = temporary.read_bytes()
                if b"Date,Store Number," not in data[:65536]:
                    raise RuntimeError("Portal did not return a supported detail CSV")
                digest = hashlib.sha256(data).hexdigest()
                atomic(inbox / (digest + ".csv"), data)
                temporary.unlink()
                checkpoint["csv"] = "Downloaded"
                save()
            else:
                checkpoint["csv"] = "Blocked: export dialog/detail download mapping needs completion"
            # Page evidence is private, resumable source context, not a parsed invoice or job match.
            # Re-read all pages on resume so changed ordering cannot silently skip a receipt.
            seen = set()
            total_seen = 0
            for number in range(1, int(portal.get("max_pages", 100)) + 1):
                visible = page.get_by_text(re.compile(r"Viewing\s+\d+\s*-\s*\d+\s+of\s+\d+")).inner_text()
                match = re.search(r"Viewing\s+(\d+)\s*-\s*(\d+)\s+of\s+(\d+)", visible)
                if not match:
                    raise RuntimeError("Purchase pagination metadata is unavailable")
                first, last, total = map(int, match.groups())
                if first != total_seen + 1 or last < first or last > total:
                    raise RuntimeError("Purchase pagination is inconsistent; collection remains partial")
                table = page.get_by_role("table").first
                text = table.inner_text()
                digest = hashlib.sha256(text.encode()).hexdigest()
                if digest in seen:
                    raise RuntimeError("Repeated purchase page; collection remains partial")
                seen.add(digest)
                # Page text includes returns/totals if displayed, but does not prove payment status.
                atomic(state / ("page-" + digest + ".json"), json.dumps({"page": number, "first": first, "last": last, "total": total, "text": text}).encode())
                receipt_selector = portal.get("receipt_download_selector")
                if receipt_selector:
                    # Only a separately observed download control may be configured. No guessed
                    # PDF routes or signed URLs. The dashboard, not this loop, resolves identity.
                    links = table.get_by_role("link")
                    link_count = links.count()
                    if link_count != last - first + 1:
                        raise RuntimeError("Receipt links do not reconcile to purchase pagination")
                    for index in range(link_count):
                        page.get_by_role("table").first.get_by_role("link").nth(index).click()
                        control = page.locator(receipt_selector)
                        if control.count() != 1:
                            raise RuntimeError("Observed receipt-download control unavailable; partial originals retained")
                        with page.expect_download() as pending_receipt:
                            control.click()
                        receipt = pending_receipt.value
                        temporary_receipt = inbox / "receipt.part"
                        receipt.save_as(str(temporary_receipt))
                        if temporary_receipt.stat().st_size > MAX_BYTES:
                            raise RuntimeError("Receipt exceeds dashboard upload limit")
                        receipt_bytes = temporary_receipt.read_bytes()
                        if not receipt_bytes.startswith(b"%PDF-"):
                            raise RuntimeError("Receipt download is not an original PDF")
                        receipt_hash = hashlib.sha256(receipt_bytes).hexdigest()
                        atomic(inbox / (receipt_hash + ".pdf"), receipt_bytes)
                        temporary_receipt.unlink()
                        checkpoint["receipts"] = "Downloaded originals; identity/payment interpretation deferred to dashboard"
                        save()
                        page.go_back(wait_until="domcontentloaded")
                        returned = page.get_by_role("table").first.inner_text()
                        if hashlib.sha256(returned.encode()).hexdigest() != digest:
                            raise RuntimeError("Purchase page changed after receipt navigation; replay range on resume")
                checkpoint["completed_pages"] = number
                checkpoint["last_range"] = [first, last, total]
                total_seen = last
                save()
                if last == total:
                    checkpoint["history_pages"] = "Selected range traversed"
                    break
                next_page = page.get_by_role("button", name="Skip to Next Page", exact=True)
                if not next_page.is_enabled():
                    raise RuntimeError("Next page unavailable before expected end")
                next_page.click()
            else:
                raise RuntimeError("Page cap reached; collection remains partial")
            # There is no default observed receipt-download mapping. Never click Pay, Add Receipt,
            # PO/job edit, account, card or purchasing controls. PDFs already downloaded by
            # the owner are submitted through explicit dashboard purchaseId manifests.
            if not portal.get("receipt_download_selector"):
                checkpoint["receipts"] = "Unavailable: receipt-download mapping not established; manual originals supported"
            checkpoint["status"] = "Partial"
            if blocked:
                checkpoint["blocked_read"] = True
            save()
            raise RuntimeError("Partial collection: receipt/return/payment evidence is unavailable; downloaded CSV remains queued for intake")
        finally:
            browser.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--csv", action="append", default=[])
    parser.add_argument("--manifest", help="Private JSON list of file, kind and dashboard-selected purchaseId for originals")
    parser.add_argument("--collect", action="store_true")
    parser.add_argument("--login", action="store_true")
    parser.add_argument("--use-current-range", action="store_true")
    parser.add_argument("--collect-only", action="store_true", help="Archive originals without calling dashboard intake")
    args = parser.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    if not config.get("enabled"):
        raise RuntimeError("Collector is disabled")
    state = private_directory(config["state_directory"])
    inbox = private_directory(config["inbox_directory"])
    with single_run(state), sqlite3.connect(state / "receipts.sqlite") as db:
        db.execute("create table if not exists receipts (receipt_key text primary key, document_id text not null, acknowledged_at real not null)")
        if os.name != "nt":
            (state / "receipts.sqlite").chmod(0o600)
        partial = False
        if args.collect or args.login:
            if not config.get("portal", {}).get("enabled"):
                raise RuntimeError("Portal adapter is disabled")
            try:
                collect(config, state, inbox, args.use_current_range, args.login)
            except Exception:
                # Dependencies, CAPTCHA, auth and changing UI all remain explicit partial outcomes.
                partial = True
                print("Portal collection incomplete; saved originals will still be submitted. See private collection checkpoint.", file=sys.stderr)
        submitted = 0
        for source in args.csv:
            # Copy initial/manual exports into the private resumable inbox before upload.
            data = Path(source).read_bytes()
            if len(data) > MAX_BYTES:
                raise RuntimeError("Manual source exceeds dashboard upload limit")
            atomic(inbox / (hashlib.sha256(data).hexdigest() + ".csv"), data)
        if args.collect_only:
            print(json.dumps({"collection": "Partial" if partial else "NotRequested", "originals_queued": len(list(inbox.glob("*.csv"))) + len(list(inbox.glob("*.pdf"))), "dashboard_handoff": "Deferred", "complete_history": False}))
            return 2 if partial else 0
        for source in sorted(inbox.glob("*.csv")):
            submitted += int(submit(config, db, source))
        for source in sorted(inbox.glob("*.pdf")):
            # Unlinked originals enter dashboard evidence review, never an inferred invoice/job.
            submitted += int(submit(config, db, source, "Receipt"))
        if args.manifest:
            for item in json.loads(Path(args.manifest).read_text(encoding="utf-8")):
                submitted += int(submit(config, db, item["file"], item["kind"], item.get("purchaseId", "")))
        print(json.dumps({"acknowledged_originals": submitted, "collection": "Partial" if partial else "NotRequested" if not args.collect else "Partial", "complete_history": False}))
        return 2 if partial else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        print("Collection/intake did not complete. No complete-history or accounting result is claimed; inspect protected runtime configuration and retained checkpoint.", file=sys.stderr)
        sys.exit(1)
