#!/usr/bin/env python3
"""Decks & Docks read-only collector and retry-safe dashboard handoff.

No supplier credentials are accepted on the command line. Browser authentication
belongs to an interactive session in the dedicated protected browser profile.
"""
from __future__ import annotations

import argparse
import datetime as dt
import decimal
import hashlib
import http.client
import ipaddress
import json
import os
from pathlib import Path
import re
import sqlite3
import socket
import ssl
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import uuid

MAX_PDF = 20 * 1024 * 1024
MAX_METADATA = 512 * 1024
D = decimal.Decimal


class Blocked(Exception):
    def __init__(self, code: str):
        self.code = code


def private_directory(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True)
    if os.name != "nt":
        path.chmod(0o700)
    return path


def read_protected(path: Path) -> str:
    if path.is_symlink() or (os.name != "nt" and path.stat().st_mode & 0o077):
        raise Blocked("protected_file_permissions_required")
    return path.read_text(encoding="utf-8")


def money(value: str) -> D:
    value = value.strip().replace("$", "").replace(",", "")
    if value.startswith("(") and value.endswith(")"):
        value = "-" + value[1:-1]
    return D(value)


def first_match(text: str, pattern: str) -> str | None:
    found = re.search(pattern, text, re.I | re.M)
    return found.group(1).strip() if found else None


def date_value(text: str | None) -> str | None:
    if text:
        for fmt in ("%m/%d/%Y", "%m/%d/%y", "%m-%d-%Y", "%Y-%m-%d"):
            try:
                return dt.datetime.strptime(text, fmt).date().isoformat()
            except ValueError:
                pass
    return None


def run_text(arguments: list[str], timeout: int = 60) -> str:
    try:
        result = subprocess.run(arguments, capture_output=True, timeout=timeout, check=False)
    except FileNotFoundError:
        raise Blocked("extraction_dependency_missing") from None
    except subprocess.TimeoutExpired:
        raise Blocked("extraction_timeout") from None
    if result.returncode:
        # Tool output may contain invoice details; never relay it to Hermes logs.
        raise Blocked("pdf_extraction_failed")
    if len(result.stdout) > 4 * 1024 * 1024:
        raise Blocked("extracted_text_limit")
    return result.stdout.decode("utf-8", errors="replace")


def extract_text(path: Path, settings: dict) -> str:
    max_pages = max(1, min(int(settings.get("max_pages", 30)), 100))
    if settings.get("engine", "pdftotext") == "pypdf":
        try:
            from pypdf import PdfReader
        except ImportError:
            raise Blocked("extraction_dependency_missing") from None
        try:
            reader = PdfReader(path)
            if reader.is_encrypted:
                raise Blocked("encrypted_invoice_requires_review")
            if len(reader.pages) > max_pages:
                raise Blocked("invoice_page_limit_requires_review")
            text = "\n\f\n".join(page.extract_text(extraction_mode="layout") or "" for page in reader.pages)
            if len(text.encode("utf-8")) > 4 * 1024 * 1024:
                raise Blocked("extracted_text_limit")
        except Blocked:
            raise
        except Exception:
            raise Blocked("pdf_extraction_failed") from None
    else:
        text = run_text([settings.get("pdftotext", "pdftotext"), "-layout", "-f", "1", "-l", str(max_pages), str(path), "-"])
    if text.strip():
        return text
    if not settings.get("ocr_enabled", False):
        return ""
    with tempfile.TemporaryDirectory(prefix="supplier-ocr-") as temporary:
        prefix = Path(temporary) / "page"
        run_text([settings.get("pdftoppm", "pdftoppm"), "-f", "1", "-l", str(max_pages), "-r", "150", "-png", str(path), str(prefix)], 120)
        pages = sorted(Path(temporary).glob("page-*.png"))
        return "\n\f\n".join(run_text([settings.get("tesseract", "tesseract"), str(page), "stdout", "--psm", "6"]) for page in pages)


def parse_invoice(text: str, settings: dict) -> dict:
    """Conservative extraction: incomplete/unknown layouts always reach review.

    A verified portal-specific line_pattern can replace the initial candidates.
    Named groups: sku, description, quantity, unitPrice, lineTotal, unit (optional).
    """
    number = first_match(text, r"\bINVOICE\s*(?:NUMBER|NO\.?|#)\s*:?\s*([A-Z0-9][A-Z0-9/-]{0,99})") or ""
    invoice_date = date_value(first_match(text, r"\bINVOICE\s+DATE\s*:?\s*([0-9/-]+)"))
    due_date = date_value(first_match(text, r"\bDUE\s+DATE\s*:?\s*([0-9/-]+)"))
    job = first_match(text, r"\bJOB\s*(?:NUMBER|NO\.?|#)\s*:?\s*([^\r\n]{1,100})") or ""
    your_reference = first_match(text, r"^[ \t]*YOUR[ \t]+REF(?:ERENCE)?\.?[ \t]*:?[ \t]+([^\r\n]{1,500})$") or ""
    warnings = []
    if not re.search(r"DECKS\s*(?:&|AND)\s*DOCKS", text, re.I):
        warnings.append("Supplier identity could not be confirmed from the PDF.")
    configured_pattern = settings.get("line_pattern", "")
    numeric = r"\(?-?\$?[\d,]+(?:\.\d{1,4})?\)?"
    candidates = [configured_pattern] if configured_pattern else [
        rf"^\s*(?P<sku>\S+)\s{{2,}}(?P<description>.+?)\s{{2,}}(?P<quantity>{numeric})\s+(?P<unit>[A-Za-z/]+)\s+(?P<unitPrice>{numeric})\s+(?P<lineTotal>{numeric})\s*$",
        rf"^\s*(?P<quantity>{numeric})\s+(?P<sku>\S+)\s{{2,}}(?P<description>.+?)\s{{2,}}(?P<unitPrice>{numeric})\s+(?P<lineTotal>{numeric})\s*$",
    ]
    if not configured_pattern:
        warnings.append("Invoice layout has not been confirmed; compare every extracted line and purchase unit with the original PDF.")
    patterns = [re.compile(pattern, re.I) for pattern in candidates]
    lines = []
    for raw in text.splitlines():
        for pattern in patterns:
            found = pattern.match(raw)
            if found:
                fields = found.groupdict()
                try:
                    quantity, price, total = (money(fields[name]) for name in ("quantity", "unitPrice", "lineTotal"))
                except (decimal.InvalidOperation, KeyError):
                    warnings.append("A candidate invoice line could not be parsed.")
                    break
                unit = fields.get("unit") or ""
                if not unit:
                    warnings.append("A purchase unit is missing; confirm its unit and package quantity.")
                lines.append({"sku": fields["sku"][:100], "description": fields["description"][:300],
                              "purchaseUnit": unit, "packageQuantity": 1,
                              "quantity": float(quantity), "unitPrice": float(price), "lineTotal": float(total)})
                break
    def total_for(label: str) -> D:
        value = first_match(text, rf"^\s*(?:{label})\s*:?\s*({numeric})\s*$")
        if value is None:
            warnings.append("Confirm the invoice totals, including tax, freight and discounts.")
        return money(value) if value else D(0)
    tax = total_for(r"SALES\s+TAX|TAX")
    freight = total_for(r"FREIGHT|DELIVERY(?:\s+CHARGE)?|SHIPPING")
    discount = total_for(r"DISCOUNT(?:\s+AMOUNT)?")
    total = total_for(r"INVOICE\s+TOTAL|TOTAL\s+AMOUNT|GRAND\s+TOTAL|TOTAL")
    if not lines:
        warnings.append("No complete item lines were extracted. Enter them from the original invoice.")
    if not number or not invoice_date or not due_date:
        warnings.append("Confirm the invoice number, invoice date and due date.")
    if len(lines) > 1000:
        raise Blocked("invoice_line_limit")
    if abs(sum(D(str(line["lineTotal"])) for line in lines) + tax + freight - discount - total) > D("0.01"):
        warnings.append("Extracted line amounts do not reconcile to the invoice total.")
    warnings.append("Confirm local operation, job assignment and amount already paid before AP posting.")
    return {"invoiceNumber": number, "invoiceDate": invoice_date, "dueDate": due_date,
            "localOperationId": None, "yourReference": your_reference,
            "payingEntityCode": "", "jobNumber": job, "currency": "USD",
            "isCreditNote": bool(re.search(r"\bCREDIT\s+(?:MEMO|NOTE)\b", text, re.I)),
            "tax": float(tax), "freight": float(freight), "discount": float(discount), "total": float(total),
            "amountPaid": 0, "paymentStateVerified": False, "extractionComplete": False,
            "warnings": list(dict.fromkeys(warnings)), "lines": lines}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def dashboard_opener(config: dict):
    address = config.get("dashboard_connect_address", "")
    if not address:
        return urllib.request.build_opener(NoRedirect())
    try:
        if not ipaddress.ip_address(address).is_private:
            raise ValueError()
    except ValueError:
        raise Blocked("dashboard_origin_must_be_private_ip") from None
    expected_host = urllib.parse.urlsplit(config["dashboard_url"]).hostname

    class PrivateOrigin(urllib.request.HTTPSHandler):
        def https_open(self, request):
            def connection(host, **kwargs):
                if urllib.parse.urlsplit("https://" + host).hostname != expected_host:
                    raise Blocked("dashboard_origin_host_changed")
                client = http.client.HTTPSConnection(host, context=ssl.create_default_context(), **kwargs)
                # Keep the original Host/SNI and normal certificate verification.
                # Only the TCP destination uses the existing private TLS proxy.
                client._create_connection = lambda destination, *args, **kw: socket.create_connection((address, destination[1]), *args, **kw)
                return client
            return self.do_open(connection, request)

    return urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(), PrivateOrigin())


def handoff(config: dict, key: str, pdf: bytes, payload: dict, refresh_existing: bool = False) -> dict:
    base = config["dashboard_url"].rstrip("/")
    parsed = urllib.parse.urlsplit(base)
    if parsed.scheme != "https" or parsed.username or parsed.password or parsed.query or parsed.fragment or parsed.path not in ("", "/"):
        raise Blocked("dashboard_https_origin_required")
    metadata = json.dumps(payload, allow_nan=False, separators=(",", ":")).encode()
    if len(metadata) > MAX_METADATA:
        raise Blocked("invoice_metadata_limit")
    boundary = "supplier-" + uuid.uuid4().hex
    refresh_part = f'\r\n--{boundary}\r\nContent-Disposition: form-data; name="refreshExtraction"\r\n\r\ntrue'.encode() if refresh_existing else b""
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"invoice\"\r\n\r\n".encode() + metadata
            + refresh_part
            + f"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"document\"; filename=\"invoice.pdf\"\r\nContent-Type: application/pdf\r\n\r\n".encode()
            + pdf + f"\r\n--{boundary}--\r\n".encode())
    request = urllib.request.Request(base + "/api/supplier-invoices/decks-docks", data=body,
                                    headers={"Content-Type": f"multipart/form-data; boundary={boundary}", "X-CCV-Supplier-Key": key}, method="POST")
    try:
        with dashboard_opener(config).open(request, timeout=90) as response:
            result = json.loads(response.read(65536))
    except urllib.error.HTTPError as error:
        raise Blocked("dashboard_http_" + str(error.code)) from None
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError):
        raise Blocked("dashboard_handoff_uncertain_retry_safe") from None
    if not isinstance(result.get("importId"), int) or not isinstance(result.get("status"), str):
        raise Blocked("dashboard_receipt_invalid")
    return result


def allowed_url(url: str, hosts: set[str]) -> bool:
    parsed = urllib.parse.urlsplit(url)
    return parsed.scheme == "https" and parsed.hostname in hosts and not parsed.username and not parsed.password


def collect_portal(config: dict, inbox: Path) -> int:
    portal = config.get("portal", {})
    if not portal.get("enabled", False):
        return 0
    if portal.get("adapter") == "suppli":
        from suppli_portal import collect_suppli, SuppliPortalError
        try:
            return collect_suppli(config, inbox, MAX_PDF)
        except SuppliPortalError as error:
            raise Blocked(error.code) from None
    start = portal.get("invoice_list_url", "")
    parsed = urllib.parse.urlsplit(start)
    origin = f"{parsed.scheme}://{parsed.netloc}"
    if not parsed.hostname or not allowed_url(start, {parsed.hostname}):
        raise Blocked("portal_https_url_required")
    tenant_path = portal.get("tenant_path", "")
    if tenant_path and not parsed.path.startswith(tenant_path):
        raise Blocked("portal_supplier_tenant_required")
    if not portal.get("authenticated_selector") or not portal.get("invoice_link_selector"):
        raise Blocked("portal_invoice_selectors_required")
    allowed_hosts = {parsed.hostname, *portal.get("allowed_download_hosts", [])}
    profile = private_directory(Path(portal["profile_directory"]))
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        raise Blocked("playwright_dependency_missing") from None
    count, visited, invoice_urls = 0, set(), set()
    with sync_playwright() as engine:
        launch = {"headless": True, "accept_downloads": False, "chromium_sandbox": True}
        if portal.get("browser_executable_path"):
            launch["executable_path"] = portal["browser_executable_path"]
        context = engine.chromium.launch_persistent_context(str(profile), **launch)
        try:
            # The collector is read-only. Supplier payment/order/administration POSTs are blocked.
            def read_only(route):
                if route.request.method not in ("GET", "HEAD", "OPTIONS"):
                    route.abort()
                else:
                    route.continue_()
            context.route("**/*", read_only)
            page = context.new_page()
            next_url = start
            for _ in range(max(1, min(int(portal.get("max_pages", 50)), 200))):
                if next_url in visited:
                    raise Blocked("portal_pagination_loop")
                visited.add(next_url)
                page.goto(next_url, wait_until="domcontentloaded", timeout=45000)
                current_url = urllib.parse.urlsplit(page.url)
                if current_url.netloc != parsed.netloc or (tenant_path and not current_url.path.startswith(tenant_path)):
                    raise Blocked("supplier_authentication_required")
                if "/auth/" in current_url.path:
                    raise Blocked("supplier_authentication_required")
                if portal.get("login_selector") and page.locator(portal["login_selector"]).is_visible():
                    raise Blocked("supplier_authentication_required")
                try:
                    page.locator(portal["authenticated_selector"]).first.wait_for(state="visible", timeout=15000)
                except Exception:
                    raise Blocked("supplier_authentication_required") from None
                for link in page.locator(portal["invoice_link_selector"]).all():
                    href = link.get_attribute("href")
                    if not href:
                        raise Blocked("portal_invoice_download_mapping_required")
                    url = urllib.parse.urljoin(page.url, href)
                    if not allowed_url(url, allowed_hosts):
                        raise Blocked("portal_download_host_not_allowed")
                    if url in invoice_urls:
                        continue
                    invoice_urls.add(url)
                    if count >= max(1, min(int(portal.get("max_invoices", 200)), 1000)):
                        raise Blocked("portal_invoice_limit_incomplete")
                    response = context.request.get(url, max_redirects=0, timeout=45000)
                    if response.status in (401, 403) or 300 <= response.status < 400:
                        raise Blocked("supplier_authentication_or_redirect_required")
                    if not response.ok:
                        raise Blocked("supplier_download_failed")
                    if int(response.headers.get("content-length", "0")) > MAX_PDF:
                        raise Blocked("supplier_document_size_limit")
                    data = response.body()
                    response.dispose()
                    if len(data) > MAX_PDF or not data.startswith(b"%PDF-"):
                        raise Blocked("supplier_download_not_supported_pdf")
                    digest = hashlib.sha256(data).hexdigest()
                    target = inbox / (digest + ".pdf")
                    if not target.exists():
                        with target.open("xb") as output:
                            output.write(data)
                        if os.name != "nt":
                            target.chmod(0o600)
                    count += 1
                selector = portal.get("next_page_link_selector", "")
                next_link = page.locator(selector) if selector else None
                if next_link is None or next_link.count() == 0:
                    break
                href = next_link.first.get_attribute("href")
                if not href:
                    raise Blocked("portal_pagination_mapping_required")
                next_url = urllib.parse.urljoin(page.url, href)
                if not next_url.startswith(origin + "/"):
                    raise Blocked("portal_pagination_origin_changed")
                if tenant_path and not urllib.parse.urlsplit(next_url).path.startswith(tenant_path):
                    raise Blocked("portal_supplier_tenant_changed")
            else:
                raise Blocked("portal_page_limit_incomplete")
        finally:
            context.close()
    return count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True)
    parser.add_argument("--refresh-existing", action="store_true", help="Re-extract cached originals; only untouched fields on pending dashboard imports can change.")
    args = parser.parse_args()
    os.umask(0o077)
    config = json.loads(read_protected(Path(args.config)))
    if not config.get("enabled", False):
        print(json.dumps({"status": "disabled", "submitted": 0}))
        return 0
    state = private_directory(Path(config["state_directory"]))
    inbox = private_directory(Path(config["inbox_directory"]))
    # Cross-process lease releases on process exit, including crashes.
    import fcntl
    with (state / "worker.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise Blocked("worker_already_running") from None
        key = os.environ.get("CCV_SUPPLIER_INGESTION_KEY") or read_protected(Path(config["ingestion_key_file"])).strip()
        if not key:
            raise Blocked("ingestion_key_not_configured")
        db = sqlite3.connect(state / "receipts.sqlite3")
        db.execute("CREATE TABLE IF NOT EXISTS receipts (sha256 TEXT PRIMARY KEY, import_id INTEGER NOT NULL, status TEXT NOT NULL, submitted_at TEXT NOT NULL)")
        submitted = refreshed = acknowledged = 0
        collection_error = None
        try:
            if not args.refresh_existing:
                collect_portal(config, inbox)
        except Blocked as error:
            # Still hand off PDFs retrieved before a pagination/authentication interruption.
            collection_error = error
        for path in sorted(inbox.glob("*.pdf")):
            if path.is_symlink() or path.stat().st_size > MAX_PDF:
                raise Blocked("inbox_document_not_supported")
            data = path.read_bytes()
            if not data.startswith(b"%PDF-"):
                raise Blocked("inbox_document_not_pdf")
            digest = hashlib.sha256(data).hexdigest()
            if not args.refresh_existing and db.execute("SELECT 1 FROM receipts WHERE sha256=?", (digest,)).fetchone():
                continue
            extraction = config.get("extraction", {})
            if extraction.get("parser", "decks-docks-positioned") == "decks-docks-positioned":
                from decks_docks_parser import parse_pdf
                payload = parse_pdf(path, extraction)
            else:
                payload = parse_invoice(extract_text(path, extraction), extraction)
            receipt = handoff(config, key, data, payload, refresh_existing=args.refresh_existing)
            # Checkpoint only after the dashboard has durably acknowledged the document.
            with db:
                db.execute("INSERT INTO receipts VALUES (?,?,?,?) ON CONFLICT(sha256) DO UPDATE SET import_id=excluded.import_id,status=excluded.status,submitted_at=excluded.submitted_at", (digest, receipt["importId"], receipt["status"], dt.datetime.now(dt.timezone.utc).isoformat()))
            acknowledged += 1
            submitted += not receipt.get("duplicate", False)
            refreshed += bool(receipt.get("extractionRefreshed", False))
        db.close()
        if collection_error:
            raise collection_error
        print(json.dumps({"status": "completed", "submitted": submitted, "refreshed": refreshed,
                          "acknowledged": acknowledged, "ap_approval": "dashboard_review"}))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Blocked as error:
        print(json.dumps({"status": "blocked", "reason": error.code}))
        sys.exit(2)
    except Exception as error:
        # Never print exception messages, URLs, invoice content, cookies or headers.
        print(json.dumps({"status": "failed", "error_type": type(error).__name__}))
        sys.exit(1)
