"""Read-only Suppli invoice UI mapping observed on the Decks & Docks portal.

Only invoice-number buttons, VIEW INVOICE and the panel close control are clicked.
Account identifiers and signed PDF URLs are never logged or written to source.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import time
import urllib.parse
import uuid


class SuppliPortalError(Exception):
    def __init__(self, code: str):
        self.code = code


def read_only_request(method: str, url: str, body: str | None = None) -> bool:
    if method in ("GET", "HEAD", "OPTIONS"):
        return True
    endpoint = urllib.parse.urlsplit(url)
    if method != "POST" or endpoint.scheme != "https" or endpoint.hostname != "core.gosuppli.com" \
            or endpoint.path != "/graphql" or endpoint.port not in (None, 443) or endpoint.username or endpoint.password:
        return False
    if not body or len(body.encode()) > 512 * 1024:
        return False
    try:
        from graphql import parse, get_operation_ast, OperationType
        payload = json.loads(body)
        requests = payload if isinstance(payload, list) else [payload]
        if not requests or len(requests) > 10:
            return False
        for request in requests:
            if not isinstance(request, dict) or not isinstance(request.get("query"), str):
                return False  # Unknown persisted operations remain blocked.
            operation = get_operation_ast(parse(request["query"]), request.get("operationName"))
            if operation is None or operation.operation != OperationType.QUERY:
                return False
        return True
    except Exception:
        return False


def collect_suppli(config: dict, inbox: Path, max_pdf_bytes: int) -> int:
    portal = config["portal"]
    login = urllib.parse.urlsplit(portal["login_url"])
    if login.scheme != "https" or login.hostname != "customer.gosuppli.com" or login.path != "/decks-docks/auth/login":
        raise SuppliPortalError("suppli_login_mapping_invalid")
    # This is a protected deployment setting, not a customer identifier in source.
    account_path = portal.get("account_path", "").rstrip("/")
    if not re.fullmatch(r"/decks-docks/[A-Za-z0-9_-]+", account_path) or account_path == "/decks-docks/auth":
        raise SuppliPortalError("suppli_account_path_required")
    origin = "https://customer.gosuppli.com"
    allowed_hosts = {"suppli-prod.s3.amazonaws.com", "customer.gosuppli.com"}
    allowed_hosts.update(portal.get("allowed_download_hosts", []))
    profile = Path(portal["profile_directory"])
    profile.mkdir(parents=True, exist_ok=True)
    if os.name != "nt":
        profile.chmod(0o700)
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        raise SuppliPortalError("playwright_dependency_missing") from None
    cache = sqlite3.connect(Path(config["state_directory"]) / "portal-downloads.sqlite3")
    cache.execute("CREATE TABLE IF NOT EXISTS downloads (identity_hash TEXT PRIMARY KEY, pdf_hash TEXT NOT NULL, view TEXT NOT NULL, fetched_at REAL NOT NULL)")
    downloaded = 0
    refresh_seconds = max(3600, int(portal.get("refresh_existing_hours", 24)) * 3600)
    max_downloads = max(1, min(int(portal.get("max_invoices", 200)), 1000))
    failure = None
    blocked_requests = []
    try:
        with sync_playwright() as engine:
            launch = {"headless": True, "accept_downloads": False, "chromium_sandbox": True}
            if portal.get("browser_executable_path"):
                launch["executable_path"] = portal["browser_executable_path"]
            context = engine.chromium.launch_persistent_context(str(profile), **launch)
            try:
                def read_only(route):
                    if not read_only_request(route.request.method, route.request.url, route.request.post_data):
                        request_url = urllib.parse.urlsplit(route.request.url)
                        # Record only endpoint shape, never headers, bodies, query
                        # strings, customer identifiers or signed document URLs.
                        path = re.sub(r"[0-9a-fA-F-]{8,}|\d+", "[id]", request_url.path)
                        blocked_requests.append({"method": route.request.method, "host": request_url.hostname, "path": path})
                        route.abort()
                    else:
                        route.continue_()
                context.route("**/*", read_only)
                page = context.new_page()
                for view in ("outstanding", "paid"):
                    page.goto(origin + account_path + "/invoices/" + view, wait_until="domcontentloaded", timeout=45000)
                    current = urllib.parse.urlsplit(page.url)
                    if current.netloc != login.netloc or not current.path.startswith(account_path + "/") or "/auth/" in current.path:
                        raise SuppliPortalError("supplier_authentication_required")
                    survey = page.get_by_role("heading", name="Were you able to complete what you came here to do?", exact=True)
                    if survey.count() and survey.is_visible():
                        page.get_by_role("button", name="Close", exact=True).click()
                    try:
                        page.locator('nav[aria-label="Tabs"]').wait_for(state="visible", timeout=20000)
                        page.locator('[role="grid"][aria-rowcount]').wait_for(state="visible", timeout=30000)
                        # Suppli currently renders the complete invoice history in an
                        # auto-height MUI grid, rather than next-page anchors.
                        page.wait_for_function("""() => {
                            const grid = document.querySelector('[role="grid"][aria-rowcount]');
                            if (!grid || grid.querySelector('[role="progressbar"], .MuiDataGrid-loadingOverlay')) return false;
                            const expected = Number(grid.getAttribute('aria-rowcount')) - 1;
                            const actual = grid.querySelectorAll('[role="row"][data-id]').length;
                            return expected > 0 && actual === expected
                                || expected === 0 && /no rows|no invoices/i.test(grid.textContent || '');
                        }""", timeout=30000)
                    except Exception as error:
                        diagnostics = {
                            "error_type": type(error).__name__,
                            "auth_redirect": "/auth/" in urllib.parse.urlsplit(page.url).path,
                            "tabs": page.locator('nav[aria-label="Tabs"]').count(),
                            "grids": page.locator('[role="grid"]').count(),
                            "grid_counts": page.locator('[role="grid"]').evaluate_all("elements => elements.map(e => ({expected:e.getAttribute('aria-rowcount'), rendered:e.querySelectorAll('[role=\"row\"][data-id]').length, busy:e.getAttribute('aria-busy')}))"),
                            "blocked_requests": blocked_requests[:25],
                        }
                        (Path(config["state_directory"]) / "last-grid-failure.json").write_text(json.dumps(diagnostics))
                        raise SuppliPortalError("suppli_invoice_grid_incomplete_or_layout_changed") from None
                    rows = page.locator('[role="grid"] [role="row"][data-id]')
                    row_ids = rows.evaluate_all("elements => elements.map(e => e.getAttribute('data-id'))")
                    if not all(row_ids) or len(row_ids) != len(set(row_ids)):
                        raise SuppliPortalError("suppli_invoice_identity_invalid")
                    for row_id in row_ids:
                        # Locate the observed DOM identity without selector interpolation.
                        target = rows.evaluate_all("(elements, id) => elements.findIndex(e => e.getAttribute('data-id') === id)", row_id)
                        if target < 0:
                            raise SuppliPortalError("suppli_invoice_grid_changed_during_collection")
                        row = rows.nth(target)
                        invoice_button = row.locator('[data-field="invoiceNumber"] button')
                        if invoice_button.count() != 1:
                            raise SuppliPortalError("suppli_invoice_number_control_changed")
                        # FC rows are account finance charges, not purchased supplies.
                        if invoice_button.inner_text().strip().upper().startswith("FC-"):
                            continue
                        identity = hashlib.sha256((account_path + ":" + row_id).encode()).hexdigest()
                        previous = cache.execute("SELECT pdf_hash,view,fetched_at FROM downloads WHERE identity_hash=?", (identity,)).fetchone()
                        if previous and previous[1] == view and time.time() - previous[2] < refresh_seconds and (inbox / (previous[0] + ".pdf")).is_file():
                            continue
                        if downloaded >= max_downloads:
                            raise SuppliPortalError("portal_invoice_limit_incomplete")
                        popup = None
                        opened_panel = False
                        try:
                            invoice_button.click()
                            opened_panel = True
                            view_button = page.get_by_role("button", name="VIEW INVOICE", exact=True)
                            view_button.wait_for(state="visible", timeout=20000)
                            with page.expect_popup(timeout=45000) as popup_event:
                                view_button.click()
                            popup = popup_event.value
                            popup.wait_for_url(lambda url: url.startswith("https://"), timeout=45000, wait_until="commit")
                            url = popup.url
                            pdf_url = urllib.parse.urlsplit(url)
                            if pdf_url.scheme != "https" or pdf_url.hostname not in allowed_hosts or pdf_url.username or pdf_url.password:
                                raise SuppliPortalError("portal_download_host_not_allowed")
                            # Use the context's existing authorized session; never extract
                            # cookies or tokens. Signed URLs stay only in process memory.
                            response = context.request.get(url, max_redirects=0, timeout=45000)
                            try:
                                if response.status in (401, 403) or 300 <= response.status < 400:
                                    raise SuppliPortalError("supplier_authentication_or_redirect_required")
                                if not response.ok:
                                    raise SuppliPortalError("supplier_download_failed")
                                if int(response.headers.get("content-length", "0")) > max_pdf_bytes:
                                    raise SuppliPortalError("supplier_document_size_limit")
                                data = response.body()
                            finally:
                                response.dispose()
                            if len(data) > max_pdf_bytes or not data.startswith(b"%PDF-"):
                                raise SuppliPortalError("supplier_download_not_supported_pdf")
                            digest = hashlib.sha256(data).hexdigest()
                            destination = inbox / (digest + ".pdf")
                            if not destination.exists():
                                temporary = inbox / (uuid.uuid4().hex + ".part")
                                try:
                                    with temporary.open("xb") as output:
                                        output.write(data)
                                        output.flush()
                                        os.fsync(output.fileno())
                                    if os.name != "nt":
                                        temporary.chmod(0o600)
                                    os.replace(temporary, destination)
                                finally:
                                    temporary.unlink(missing_ok=True)
                            with cache:
                                cache.execute("INSERT OR REPLACE INTO downloads VALUES (?,?,?,?)", (identity, digest, view, time.time()))
                            downloaded += 1
                        except SuppliPortalError as error:
                            failure = error
                        except Exception:
                            # Continue other invoices, but report incomplete collection.
                            failure = SuppliPortalError("suppli_invoice_viewer_or_download_mapping_changed")
                        finally:
                            if popup is not None:
                                popup.close()
                            if opened_panel:
                                close = page.locator("button.z-50")
                                if close.count() != 1:
                                    raise SuppliPortalError("suppli_invoice_panel_close_mapping_changed")
                                close.click()
                                close.wait_for(state="hidden", timeout=10000)
            finally:
                context.close()
    finally:
        cache.close()
    if failure:
        raise failure
    return downloaded
