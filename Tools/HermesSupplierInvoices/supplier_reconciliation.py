"""Read-only supplier balances and settlement evidence; no payment mutations."""
from __future__ import annotations
import datetime as dt
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import urllib.request
from suppli_portal import read_only_request, SuppliPortalError


def cents(value):
    result = Decimal(str(value))
    if not result.is_finite() or result != result.to_integral_value():
        raise SuppliPortalError("supplier_nonintegral_cents")
    return int(result)


def collect_snapshot(config):
    from playwright.sync_api import sync_playwright
    portal = config["portal"]
    origin = "https://customer.gosuppli.com"
    account = portal["account_path"].rstrip("/")
    # Reuse the same authenticated profile and AST-checked query-only request gate.
    gathered = {}; summaries = []
    with sync_playwright() as engine:
        context = engine.chromium.launch_persistent_context(portal["profile_directory"], headless=True,
            chromium_sandbox=True, executable_path=portal.get("browser_executable_path") or None)
        try:
            context.route("**/*", lambda route: route.continue_() if read_only_request(
                route.request.method, route.request.url, route.request.post_data) else route.abort())
            page = context.new_page()
            received = []
            def response(item):
                if item.url != "https://core.gosuppli.com/graphql": return
                try:
                    body = item.json()
                    if body.get("errors"): received.append({"error": True}); return
                    data = body.get("data", {})
                    for key in ("invoices", "credits", "paymentTransactions"):
                        if key in data and "paginationData" in data[key] and "data" in data[key]:
                            received.append({key: data[key]})
                    if "contracts" in data: summaries.extend(data["contracts"].get("data", []))
                except Exception:
                    pass
            page.on("response", response)
            for view, key in (("invoices/outstanding", "invoices"), ("invoices/paid", "invoices"),
                              ("invoices/credits", "credits"), ("payments", "paymentTransactions")):
                received.clear()
                page.goto(origin + account + "/" + view, wait_until="networkidle", timeout=60000)
                if "/auth/" in page.url or not page.url.startswith(origin + account + "/"):
                    raise SuppliPortalError("supplier_authentication_required")
                page.locator('[role="grid"][aria-rowcount]').wait_for(timeout=30000)
                page.wait_for_function("""() => {
                    const grid = document.querySelector('[role="grid"][aria-rowcount]');
                    if (!grid || grid.querySelector('[role="progressbar"],.MuiDataGrid-loadingOverlay')) return false;
                    return Number(grid.getAttribute('aria-rowcount')) - 1 === grid.querySelectorAll('[role="row"][data-id]').length;
                }""", timeout=30000)
                choices = [entry[key] for entry in received if key in entry]
                if not choices or any(entry.get("error") for entry in received):
                    raise SuppliPortalError("supplier_reconciliation_query_incomplete")
                result = choices[-1]; rows = result["data"]; pagination = result["paginationData"]
                ids = [row["id"] for row in rows]
                if pagination["totalPages"] > 1 or pagination["totalCount"] != len(rows) or len(ids) != len(set(ids)):
                    raise SuppliPortalError("supplier_reconciliation_pagination_incomplete")
                gathered[view] = rows
        finally:
            context.close()
    invoice_rows = gathered["invoices/outstanding"] + gathered["invoices/paid"]
    if len({row["id"] for row in invoice_rows}) != len(invoice_rows):
        raise SuppliPortalError("supplier_invoice_views_overlap")
    summaries = [s for s in summaries if str(s.get("referenceId", "")).upper() == account.split("/")[-1].upper()]
    contracts = {s["id"] for s in summaries}
    if len(contracts) != 1: raise SuppliPortalError("supplier_account_scope_ambiguous")
    if any(row.get("contract", {}).get("id") not in (None, next(iter(contracts))) for row in invoice_rows):
        raise SuppliPortalError("supplier_invoice_account_mismatch")
    if not summaries: raise SuppliPortalError("supplier_account_summary_missing")
    summary = summaries[-1]
    outstanding = sum(cents(r["outstandingAmountCents"]) for r in gathered["invoices/outstanding"])
    credits_available = sum(cents(r["outstandingAmountCents"]) for r in gathered["invoices/credits"])
    if outstanding != cents(summary["outstandingAmountCents"]) or credits_available != cents(summary["availableCreditAmountCents"]):
        raise SuppliPortalError("supplier_summary_does_not_reconcile")
    invoices = []
    for row in invoice_rows:
        paid = row.get("paidWith") or {}
        invoices.append({"sourceId": row["id"], "number": row["invoiceNumber"], "issueDate": row["issueDate"][:10],
            "dueDate": row["maturityDate"][:10], "recordType": row["recordType"], "amountCents": cents(row["amountCents"]),
            "outstandingCents": cents(row["outstandingAmountCents"]), "pendingCents": cents(row.get("pendingPaymentsAmountCents", 0)),
            "view": "outstanding" if row in gathered["invoices/outstanding"] else "paid",
            "settlementReference": paid.get("id"), "settlementDate": (paid.get("paymentTransaction") or {}).get("entryDate")})
    payments = []
    for row in gathered["payments"]:
        payable = row.get("payable") or {}; allocations = []
        for invoice in payable.get("invoices", []):
            for allocation in invoice.get("payableInvoiceAssignments", []):
                if allocation.get("payableId") != payable.get("id"): continue
                allocations.append({"invoiceNumber": invoice["invoiceNumber"], "sourceInvoiceId": invoice["id"],
                    "chargeCents": cents(allocation["chargeAmountCents"]),
                    "coveredCents": cents(allocation["coveredOutstandingAmountCents"]),
                    "discountCents": cents(allocation["appliedDiscountAmountCents"]),
                    "deductionCents": cents(allocation["deductionAmountCents"])})
        payments.append({"sourceId": row["id"], "status": row["status"], "displayStatus": row["displayStatus"],
            "entryDate": row["entryDate"], "settledAt": row.get("settledAt"),
            "totalCents": cents(row["totalAmountCents"]), "chargedCents": cents(row["chargeAmountCents"]),
            "creditsCents": cents(payable.get("creditsAppliedCents", 0)), "depositsCents": cents(payable.get("depositsAppliedCents", 0)),
            "allocations": allocations})
    credits = [{"sourceId": row["id"], "reference": str(row["referenceId"]), "issueDate": row["issueDate"][:10],
        "amountCents": cents(row["amountCents"]), "availableCents": cents(row["outstandingAmountCents"]),
        "status": row["balanceStatus"]} for row in gathered["invoices/credits"]]
    snapshot = {"capturedAt": dt.datetime.now(dt.timezone.utc).isoformat(), "complete": True,
        "accountHash": hashlib.sha256((account + ":" + next(iter(contracts))).encode()).hexdigest(),
        "summary": {"outstandingCents": outstanding, "availableCreditsCents": credits_available,
            "netDueCents": cents(summary["netDueAmountCents"]), "pastDueCents": cents(summary["overdueOutstandingAmountCents"]),
            "availableDepositsCents": cents(summary["availableDepositAmountCents"])},
        "invoices": invoices, "payments": payments, "credits": credits}
    directory = Path(config["state_directory"]) / "reconciliation"
    directory.mkdir(mode=0o700, exist_ok=True)
    path = directory / (dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ") + ".json")
    path.write_text(json.dumps(snapshot, sort_keys=True)); path.chmod(0o600)
    return snapshot


def reconcile(config, key, opener):
    snapshot = collect_snapshot(config)
    data = json.dumps(snapshot, allow_nan=False).encode()
    request = urllib.request.Request(config["dashboard_url"].rstrip("/") + "/api/supplier-invoices/decks-docks/reconcile",
        data=data, headers={"Content-Type": "application/json", "X-CCV-Supplier-Key": key}, method="POST")
    with opener(config).open(request, timeout=90) as response:
        result = json.loads(response.read(65536))
    if not isinstance(result.get("runId"), int): raise SuppliPortalError("supplier_reconciliation_receipt_invalid")
    return result
