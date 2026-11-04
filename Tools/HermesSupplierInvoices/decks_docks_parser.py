"""Parse the printed Decks & Docks sales-invoice columns, not flattened text order."""
from __future__ import annotations

import datetime as dt
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
from pathlib import Path
import re

VERSION = "decks-docks-positioned-v2"
AMOUNT = re.compile(r"^\(?[+-]?\$?(?:\d[\d,]*)(?:\.\d+)?\)?$")


def number(text: str) -> Decimal | None:
    text = text.strip()
    if not AMOUNT.fullmatch(text):
        return None
    negative = text.startswith("(") and text.endswith(")")
    try:
        result = Decimal(text.strip("()").replace("$", "").replace(",", ""))
        return -result if negative else result
    except InvalidOperation:
        return None


def rows(words: list[dict], tolerance: float = 2) -> list[list[dict]]:
    result: list[list[dict]] = []
    for word in sorted(words, key=lambda w: (w["top"], w["x0"])):
        if not result or abs(word["top"] - min(w["top"] for w in result[-1])) > tolerance:
            result.append([word])
        else:
            result[-1].append(word)
    return [sorted(row, key=lambda w: w["x0"]) for row in result]


def text(words: list[dict]) -> str:
    return " ".join(w["text"] for w in sorted(words, key=lambda w: w["x0"])).strip()


def unit(value: str) -> str:
    value = value.strip()
    return {"ea": "Each", "each": "Each", "l/f": "LF", "lf": "LF"}.get(value.lower(), value)


def parse_date(value: str) -> dt.date | None:
    for fmt in ("%m/%d/%Y", "%m/%d/%y", "%m-%d-%Y", "%Y-%m-%d"):
        try:
            return dt.datetime.strptime(value.strip(), fmt).date()
        except ValueError:
            pass
    return None


def header_value(page_rows: list[list[dict]], label: str, width: float) -> str:
    expected = label.lower().split()
    for row in page_rows:
        right = [word for word in row if word["x0"] > width * .60]
        tokens = [word["text"].lower().rstrip(":.") for word in right]
        if tokens[:len(expected)] == expected:
            label_words = right[:len(expected)]
            top, bottom = min(w["top"] for w in label_words), max(w["bottom"] for w in label_words)
            end = max(w["x1"] for w in label_words)
            return text([word for candidate in page_rows for word in candidate if word["x0"] > end + 2
                         and word["bottom"] >= top and word["top"] <= bottom])
    return ""


def parse_pages(pages: list[dict]) -> dict:
    warnings: list[str] = []
    evidence: dict[str, str] = {}
    items = []
    summary: dict[str, Decimal] = {}
    header: dict[str, str] = {}
    recognized_pages = 0
    legal_pages = 0
    for page_number, page in enumerate(pages, 1):
        width, height = page["width"], page["height"]
        page_rows = rows(page["words"])
        if page_number == 1:
            for key, label in (("invoiceNumber", "Invoice No"), ("invoiceDate", "Invoice Date"),
                               ("dueDate", "Due Date"), ("terms", "Terms"), ("jobNumber", "Job"), ("yourReference", "Your Ref")):
                header[key] = header_value(page_rows, label, width)

        if page_number == 1:
            for row in page_rows:
                delivery = re.search(r"Delivery\s+Address\s*:\s*(.+)", text(row), re.I)
                if delivery:
                    header["deliveryAddress"] = delivery.group(1).strip()
                    break

        footer_top = height
        for row in page_rows:
            if min(w["top"] for w in row) < height * .60:
                continue
            right = [w for w in row if w["x0"] > width * .67]
            label_words = [w for w in right if number(w["text"]) is None and w["text"] != "$"]
            rendered = text(label_words).rstrip(":")
            for key, pattern in (("subtotal", r"(?:Total\s+Amount|Invoice\s+Amount)"), ("tax", r"(?:Sales\s+Tax|Tax\s+Amount)"),
                                 ("total", r"(?:Invoice\s+Total|Total\s+Invoice)"), ("freight", r"(?:Freight|Shipping|Delivery\s+Charge)"),
                                 ("discount", r"Discount(?:\s+Amount)?"), ("netSubtotal", r"Invoice\s+After\s+Discount")):
                if not label_words or not re.fullmatch(pattern, rendered, re.I):
                    continue
                label_top, label_bottom = min(w["top"] for w in label_words), max(w["bottom"] for w in label_words)
                candidates = [(w, number(w["text"])) for w in page["words"] if w["x0"] > width * .82
                              and w["bottom"] >= label_top and w["top"] <= label_bottom]
                candidates = [(w, value) for w, value in candidates if value is not None]
                if len(candidates) != 1:
                    warnings.append(f"Page {page_number}: the printed {key} amount is ambiguous.")
                    continue
                value = candidates[0][1]
                if key in summary and summary[key] != value:
                    warnings.append(f"Printed {key} amounts differ between pages; check the original invoice.")
                summary[key] = value
                evidence[key] = f"Printed summary, page {page_number}"
                footer_top = min(footer_top, min(w["top"] for w in row))

        heading = None
        for row in page_rows:
            labels = {w["text"].lower(): w for w in row}
            quantity = next((w for w in row if w["text"].lower() in ("qty/footage", "quantity", "qty")), None)
            if quantity and all(label in labels for label in ("description", "price", "total")) and ("uom" in labels or "per" in labels):
                heading = {**labels, "quantity": quantity}
                heading["uom"] = labels.get("uom", labels.get("per"))
                break
        if heading is None:
            compact = re.sub(r"[^a-z]", "", " ".join(w["text"] for w in sorted(page["words"], key=lambda w: (w["top"], w["x0"]))).lower())
            if page_number > 1 and any(label in compact for label in ("termsandconditions", "termsconditions", "conditionsofsale")):
                legal_pages += 1
                continue
            warnings.append(f"Page {page_number}: the expected invoice table headings were not found.")
            continue
        recognized_pages += 1
        description_start = heading["description"]["x0"] - 1
        quantity_start = heading["quantity"]["x0"] - 8
        price_start = heading["price"]["x0"] - 10
        price_unit_start = heading["uom"]["x0"] - 5
        total_start = heading["total"]["x0"] - 10
        table_top = max(w["bottom"] for w in heading.values()) + 2
        body = [w for w in page["words"] if table_top < w["top"] < footer_top - 2]
        anchors = sorted([w for w in body if description_start - 40 < w["x0"] < description_start - 8
                          and re.fullmatch(r"\d{1,4}", w["text"])], key=lambda w: w["top"])
        for index, anchor in enumerate(anchors):
            row_number = anchor["text"]
            next_top = anchors[index + 1]["top"] - 2 if index + 1 < len(anchors) else footer_top - 2
            line_words = [w for w in body if abs(w["top"] - anchor["top"]) <= 2]
            def column(start: float, end: float) -> list[dict]:
                return [w for w in line_words if start <= w["x0"] < end]
            quantity_words = column(quantity_start, price_start)
            quantity_values = [(w, number(w["text"])) for w in quantity_words if number(w["text"]) is not None]
            prices = [number(w["text"]) for w in column(price_start, price_unit_start) if number(w["text"]) is not None]
            amounts = [number(w["text"]) for w in column(total_start, width - 10) if number(w["text"]) is not None]
            if len(quantity_values) != 1 or len(prices) != 1 or len(amounts) != 1:
                warnings.append(f"Page {page_number}, line {row_number}: quantity, price or line total needs review.")
                continue
            quantity = quantity_values[0][1]
            quantity_unit = unit(text([w for w in quantity_words if number(w["text"]) is None]))
            purchase_unit = unit(text([w for w in column(price_unit_start, total_start)
                                       if not re.fullmatch(r"TX\d+", w["text"], re.I)]))
            desc_words = [w for w in body if description_start <= w["x0"] < quantity_start
                          and anchor["top"] - 2 <= w["top"] < next_top]
            desc_rows = rows(desc_words)
            accepted = []
            previous_top = anchor["top"]
            for desc_row in desc_rows:
                top = min(w["top"] for w in desc_row)
                if accepted and top - previous_top > 14:
                    break
                accepted.append(text(desc_row)); previous_top = top
            description_cell = " ".join(accepted)
            split = re.match(r"^(\S+)\s+[-\u2013\u2014]\s+(.+)$", description_cell)
            if split:
                sku, description = split.groups()
            else:
                sku, description = "", description_cell
                warnings.append(f"Page {page_number}, line {row_number}: the supplier SKU is not unambiguous.")
            if sku.lower() in ("each", "ea", "lf", "uom") or re.fullmatch(r"TX\d+", sku, re.I):
                sku = ""; warnings.append(f"Page {page_number}, line {row_number}: a unit or tax code cannot be used as a SKU.")
            if len(sku) > 100 or len(description) > 300 or len(purchase_unit) > 40:
                warnings.append(f"Page {page_number}, line {row_number}: text exceeds catalog limits; compare the original.")
                sku = sku if len(sku) <= 100 else ""
                description = description[:300]; purchase_unit = purchase_unit[:40]
            price, amount = prices[0], amounts[0]
            if quantity_unit and purchase_unit and quantity_unit.lower() != purchase_unit.lower():
                warnings.append(f"Page {page_number}, line {row_number}: quantity and price use different units; confirm conversion before posting.")
            if (quantity * price).quantize(Decimal('.01'), rounding=ROUND_HALF_UP) != amount:
                warnings.append(f"Page {page_number}, line {row_number}: printed quantity times price differs from the printed line total.")
            if not purchase_unit:
                warnings.append(f"Page {page_number}, line {row_number}: purchase unit needs confirmation.")
            items.append({"sku": sku, "description": description, "purchaseUnit": purchase_unit,
                          "packageQuantity": 1, "quantity": float(quantity), "unitPrice": float(price), "lineTotal": float(amount)})

    invoice_date = parse_date(header.get("invoiceDate", ""))
    due_date = parse_date(header.get("dueDate", ""))
    terms = re.fullmatch(r"Net\s*(\d{1,3})(?:\s*Days?)?", header.get("terms", ""), re.I)
    if due_date is None and invoice_date is not None and terms:
        due_date = invoice_date + dt.timedelta(days=int(terms.group(1)))
        evidence["dueDate"] = "Derived from the printed invoice date and explicit Net terms"
    for key in ("tax", "total"):
        if key not in summary:
            warnings.append(f"The printed {key} amount was not found; do not interpret the placeholder as a confirmed zero.")
    if not header.get("invoiceNumber") or invoice_date is None or due_date is None:
        warnings.append("Confirm the invoice number, invoice date and due date against the original.")
    if not items:
        warnings.append("No complete item rows were identified; review the original document.")
    line_sum = sum(Decimal(str(line["lineTotal"])) for line in items)
    freight, discount, tax, total = (summary.get(key, Decimal(0)) for key in ("freight", "discount", "tax", "total"))
    if "subtotal" in summary and line_sum + freight - discount != summary["subtotal"]:
        if line_sum != summary["subtotal"]:
            warnings.append("Printed item totals do not reconcile to Total Amount; check missing rows, freight or discounts.")
    if "netSubtotal" in summary and summary.get("subtotal", line_sum) - discount != summary["netSubtotal"]:
        warnings.append("Printed invoice amount and discount do not reconcile to Invoice After Discount.")
    if line_sum + freight - discount + tax != total:
        warnings.append("Printed items, charges and sales tax do not reconcile to Invoice Total.")
    structural_warnings = len(warnings)
    warnings.append("Confirm purchasing location, job assignment and amount already paid before AP posting.")
    return {"invoiceNumber": header.get("invoiceNumber", ""), "invoiceDate": invoice_date.isoformat() if invoice_date else None,
            "dueDate": due_date.isoformat() if due_date else None, "localOperationId": None,
            "deliveryAddress": header.get("deliveryAddress", ""), "yourReference": header.get("yourReference", ""), "payingEntityCode": "", "jobNumber": header.get("jobNumber", ""),
            "currency": "USD", "isCreditNote": False, "tax": float(tax), "freight": float(freight), "discount": float(discount),
            "total": float(total), "amountPaid": 0, "paymentStateVerified": False,
            "extractionVersion": VERSION, "extractionComplete": bool(items) and recognized_pages + legal_pages == len(pages) and structural_warnings == 0,
            "warnings": list(dict.fromkeys(warnings))[:100], "lines": items, "extractionEvidence": evidence}


def parse_pdf(path: Path, settings: dict) -> dict:
    import pdfplumber
    maximum = max(1, min(int(settings.get("max_pages", 30)), 100))
    with pdfplumber.open(path) as document:
        if len(document.pages) > maximum:
            raise ValueError("Invoice page limit exceeded; no partial document extraction was produced.")
        return parse_pages([{"width": page.width, "height": page.height,
                             "words": page.extract_words(x_tolerance=2, y_tolerance=2)} for page in document.pages])
