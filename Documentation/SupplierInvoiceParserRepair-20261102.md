# Decks & Docks parser repair

The generic text regex confused table fragments with SKUs and did not recognize
the supplier summary labels. The default parser now uses pdfplumber word positions
and supports the observed UOM and Per layouts, their distinct summary labels,
wrapped descriptions, and larger-font invoice numbers. Quantity, purchase unit,
pricing unit, tax code, unit price and line total are read from separate columns.
SKUs come from the printed description prefix, never from Each or TX tax codes.
Explicit Net terms may supply a due date; unsupported dates/unit conversions remain
for review rather than being guessed.

Across 46 cached originals, extraction finds all invoice numbers, dates, totals,
tax entries and item collections: 295 item lines with no missing SKU identifiers.

The intake supports explicit `refreshExtraction=true`. Only Needs Review imports
without a completed review may be refreshed. Posted, rejected and existing-AP
duplicate records stay unchanged. Original extraction is immutable. Field-wise
merging preserves saved reviewer changes and preserves a changed line collection
as a whole. The last automatic baseline is retained for subsequent revisions.
Parser-version checks prevent stale open reviews from posting old data. Refresh
does not call AP posting or catalog approval.

Release `20261102-supplier-parser-184044` deployed after a fresh PostgreSQL 17 backup
and archive/hash checks. No new schema migration was required. Tests passed:
36 .NET and 13 collector/parser tests, covering merge preservation, both layouts,
font alignment, wrapped descriptions and mismatched pricing units.

The cached-original refresh acknowledged 46 documents and updated all 33 pending
extractions, skipping 13 existing-AP duplicates. All 33 pending records now have
invoice totals, tax evidence and item rows: 209 items with no missing SKUs. No saved
field corrections differed from the previous automatic baseline. Seven pending
due dates still require confirmation; flagged unit conversions and payment state
also remain subject to review. No new AP postings were created. Automatic AP posting
remains disabled. The existing daily 06:00 America/Chicago job was resumed after
the repair. Credentials, browser profile, payer/location defaults and unrelated jobs
were preserved. No messages were sent. Source remains uncommitted/unpushed.
