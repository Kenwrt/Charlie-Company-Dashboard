# Decks & Docks supplier invoice automation

## Scope and current activation state

This implementation extends the existing supplier catalog, effective-dated prices,
price review, original-document storage, vendor invoices, and invoice lines. It does
not create a second catalog or a second AP ledger. No payment, email, SMS, supplier
order, or supplier-account modification is performed.

On November 2, 2026, read-only inspection of AI01 found no supplier-invoice collector
in the Hermes scripts, installed skills or named profiles, and no supplier-invoice
job in the default, personal, Arizona, or farmland profile job files. Existing Gmail,
calendar, scouts, maintenance and advertising jobs are preserved. The worker source
is authoritative in this repository. The owner subsequently authorized integration
through the Hermes PaperClip project; installation completed on AI01, and the
existing Hermes scheduler registered job `2e5352d07606` paused. It proposes daily
06:00 America/Chicago, uses `--no-agent`, and has local-only delivery. No worker run
or live supplier/dashboard request was performed during initial installation.
Subsequently, the owner completed the dedicated Hermes sign-in, the dashboard was
deployed, 46 originals were acknowledged (33 review, 13 existing-AP duplicates), and
job `2e5352d07606` was enabled for daily 06:00 America/Chicago. Automatic AP posting
remains disabled. See `SupplierInvoiceDeployment-20261102.md` for the current result.

The owner supplied the Suppli portal URL:
`https://customer.gosuppli.com/decks-docks/auth/login`. After the owner signed in,
the outstanding and paid invoice screens and original-document navigation were
inspected. `suppli_portal.py` implements that observed UI mapping. The example
configuration includes the supplier tenant boundary; the customer-specific
`portal.account_path` remains a protected deployment setting, rather than an
account identifier committed to source. The Hermes runtime still needs its own
authorized interactive sign-in; the in-app browser session was not copied or
exported. Do not guess PDF layouts, credentials, operation assignments or jobs.

## Dashboard configuration

Disabled by default. Configure outside source control using protected settings:

| Setting | Purpose |
| --- | --- |
| `SupplierInvoices:Enabled` | Enables machine ingestion and AP-agent reads. |
| `SupplierInvoices:DecksDocksVendorId` | ID of the existing active Decks & Docks supplier. |
| `SupplierInvoices:IngestionKey` | Write-only collector credential. |
| `SupplierInvoices:ApReadKey` | Independent read-only AP-agent credential. |
| `SupplierInvoices:StoragePath` | Private, durable original-document directory. |
| `SupplierInvoices:AutoPostVerifiedInvoices` | Optional posting for complete, reconciled, verified extractions with known payment state and exact catalog mappings; false by default. |

The collector intentionally does not assert verified payment state or operation/job
assignment from an unconfirmed PDF layout. Initial Decks & Docks imports go through
administrator review even if automatic posting is enabled.

Deploying this feature requires the additive `AddSupplierInvoiceImports` migration.
It was prepared as source only. This application applies pending migrations at
startup, so do not start it against a shared database as an informal check. Applying
the migration or deploying the dashboard requires a separately authorized release.
The existing deployment procedure and rollback release still apply. Preserve the
original documents and database records together; rolling back application code
does not require dropping this additive table.

On Linux, the default document directory is
`/var/lib/charlie-company/supplier-invoices`, beneath the existing application state
directory. Ensure the service can write the configured directory during authorized
deployment. Never store documents under `wwwroot` or a replaceable release directory.
This reuses private local catalog-document storage; no R2 policies or cloud resources
are changed. No automatic document deletion is implemented. Retain originals and
import audit records, including rejected and duplicate documents, until a retention
policy is separately authorized.

## Collector workflow

Source: `Tools/HermesSupplierInvoices/`.

The default PDF parser is now `decks-docks-positioned`, using pdfplumber word
coordinates and both observed supplier layouts. The original text-regex parser is
an explicit legacy option only. See `SupplierInvoiceParserRepair-20261102.md` for
the deployed repair and refresh results. `--refresh-existing` reparses cached
originals and requests a versioned pending-only merge; it does not collect new
documents, overwrite saved review corrections, or post invoices.

The Suppli adapter allows POST only to the observed `https://core.gosuppli.com/graphql`
endpoint when graphql-core can parse and select a QUERY operation. Mutations,
subscriptions, ambiguous documents and unknown persisted operations are blocked.
No headers, variables or request bodies are logged. This is necessary because Suppli
reads invoice lists through GraphQL POST rather than GET. Numeric extraction whose
precision is unsuitable for posting is preserved in the review payload; the original
PDF is accepted while AP posting remains blocked until amounts are corrected.

1. The Hermes PaperClip installer created the private worker directory and isolated
   environment on AI01 using existing Hermes-managed Python/uv. Playwright and pypdf
   installation completed. The configured browser executable reuses the existing
   Hermes Chromium; no second browser was downloaded. `extraction.engine` defaults
   to `pypdf`. Optional `pdftotext` extraction and scanned-PDF OCR need separately
   installed Poppler/Tesseract tools. No remote OCR or paid model API is used.
2. Copy `config.example.json` to the protected runtime `config.json`, permission
   `0600`. Keep the directory `0700`. Set the existing dashboard HTTPS origin and
   protected ingestion-key file path. Do not put a key in the example file, command
   line, source, logs, or Hermes task prompt.
3. Select the supplied `suppli` adapter and configure `portal.account_path` with
   the account path shown after sign-in (supplier path followed by the customer
   identifier, without `/dashboard` or `/invoices`). Keep that customer-specific
   value only in protected runtime configuration. The adapter visits outstanding
   and paid views, opens each invoice-number side panel, then clicks `VIEW INVOICE`
   to obtain the PDF popup. Its observed document host is
   `suppli-prod.s3.amazonaws.com`. Host allowlists use exact hostnames. This adapter
   never clicks Pay, Dispute, payment controls, or invoice selection checkboxes.
4. Sign into the dedicated browser profile interactively. Passwords/MFA/CAPTCHA
   remain user-only. Do not copy another browser's credential database or session.
   No saved password or token is read into chat or passed on a command line.
5. Enable the collector only after the authorized portal/profile configuration is
   complete. The private inbox can also receive PDFs already downloaded by the owner.
   Existing portal jobs or document collectors, if subsequently found, should feed
   that inbox instead of creating another downloader.
6. Register `supplier_invoice_sync.py` as the script-only Hermes job only when
   activation is requested. Hermes requires the launcher under
   `/home/ken/.hermes/scripts` and a filename-only script reference. It launches the
   worker from `/srv/ai/hermes/supplier-invoices`; it does not restart services.
   Job `2e5352d07606` has been registered paused through the existing CLI, without
   execution. Preserve that job; do not create a duplicate when configuring activation.

Worker invocation after installation (configuration path contains no secret value):

```text
/srv/ai/hermes/supplier-invoices/venv/bin/python /srv/ai/hermes/supplier-invoices/supplier_invoices.py --config /srv/ai/hermes/supplier-invoices/config.json
```

The collector serializes concurrent runs, reads invoices without supplier mutations,
downloads bounded PDF documents, traverses configured pagination, hashes documents,
extracts text, and hands PDFs plus structured data to the dashboard. It records a
SQLite receipt only after the dashboard acknowledges a committed import. A network
timeout is retry-safe because dashboard document hashes are unique. Page/invoice
limits, authentication failure, unsupported downloads, dependencies and uncertain
handoffs produce explicit nonzero outcomes; they are never reported as an empty
successful invoice run. PDFs downloaded before a portal interruption are still
submitted before the interruption is reported. Portal links are refreshed each run;
local document hashes prevent repeat submissions.

The Suppli mapping uses `[role="row"][data-id]` identity and the
`[data-field="invoiceNumber"] button` in the MUI grid. Outstanding and paid grids
use different CSS row classes, so the adapter does not depend on those classes.
The observed grids render all rows in an auto-height list and have no next-page
controls. The collector requires rendered rows to equal `aria-rowcount` minus the
header; incomplete/virtualized grids fail explicitly rather than omit invoices.
If Suppli later adds pagination, extend that mapping before claiming full collection.
Finance-charge rows prefixed `FC-` are excluded from material-invoice ingestion;
they remain account charges for the separate accounting workflow.

PDFs are atomically saved before private download receipts are checkpointed.
Previously downloaded invoices are reused for up to 24 hours by default; a move
from outstanding to paid triggers a refresh. Refreshed PDF hashes still deduplicate
at the dashboard. A missing original, changed panel/download control, or unexpected
host reports incomplete collection. Successful prior downloads still reach the
dashboard even when a later original fails. Signed PDF URLs are held only in memory.

Initial layout candidates are conservative. `extraction.line_pattern` may specify a
verified layout with named `sku`, `description`, `quantity`, `unitPrice`, `lineTotal`,
and optional `unit` groups. Wrapped descriptions, per-hundred/per-thousand pricing,
multiple delivery charges, credits, missing labels and scanned documents require
careful review or a dedicated parser extension. Extraction never invents dates,
payment state, operation IDs, or jobs. The original PDF remains the source of truth.

## Review and posting

Administrators use `/admin/supplier-invoices` to download originals and correct
header, operation/job, payment, total and line-item data. Posting requires confirmed
payment state, an active operation, a job reference, positive quantities, compatible
purchase units/package quantities, and reconciled line/header totals. Credit notes
are retained for review and routed through the existing vendor-credit workflow.

New catalog SKUs require an explicit checkbox. A second checkbox approves reviewed
prices. Otherwise existing percentage **and** dollar thresholds can approve small
changes to known products. The trusted-API switch is never used for PDF extraction.
Conflicting prices on the same date or repeated SKUs with different prices stay in
the existing catalog review queue. Older invoices create historical prices and
cannot replace a later price. Import rows are added only when posting, so unreviewed
PDF extraction cannot enter the catalog approval queue prematurely.

Posting uses one database transaction for the AP invoice, invoice lines, product
creation, price history, import linkage and audit events. A supplier-scoped database
lock serializes collector/reviewer activity. Existing database uniqueness rules
remain the final defense against concurrent manual catalog/AP edits. Original
extraction and reviewed extraction are retained separately with reviewer and UTC
timestamp. A different PDF of an invoice already in AP is retained as a duplicate
without changing its balance, payment state, lines or prices. Case-insensitive
invoice matching is performed by the importer; legacy manual AP entry retains its
existing behavior. Duplicate documents cannot be used to overwrite a reviewed import.

## Machine interfaces and AP handoff

### Paying entity and purchasing location

Supplier invoice imports record `PayingEntityCode` independently of the existing
`LocalOperationId`. The configured default payer is Charlie Company Ventures Group
(`ventures`). A single supplier invoice remains one AP entry; reporting attributes
that entry to its purchasing location. Supplier branch headings, billing address,
customer name, and unqualified job numbers never determine the purchasing location.
The additive `AddSupplierInvoiceAssignments` migration stores payer and the original
`YourReference` on AP invoices without backfilling historical payer assumptions.
No shared database migration has been applied.

The parser preserves `Your Ref`/`Your Reference` separately from `JobNumber`.
Location routing runs on the dashboard, so a collector cannot assert that an
administrator reviewed an assignment. Default location aliases are NSH/Nashville,
MUR/Murfreesboro and KNX/Knoxville. Matching ignores case, punctuation, brackets
and spacing; full city names are accepted. Detected misspellings, transposed short
codes, conflicting locations, inactive or unconfigured locations go to review.
Typo proximity never silently corrects a location. Human review requires confirmation
of payer/location and an explanation when overriding an unresolved/different match.
Audit events retain reviewer plus prior/new payer and location IDs. Original extracted
reference and reviewed reference remain available in the import snapshots.

The supplier-account routing configuration lives under `SupplierInvoices`:

| Setting | Initial behavior |
| --- | --- |
| `DefaultPayingEntityCode` | `ventures` |
| `PayingEntities` | Configured payer code-to-display-name dictionary |
| `DefaultLocationSlug` | `nashville` |
| `AllowSingleLocationDefault` | `true` |
| `ParticipatingLocationSlugs` | Only `nashville` initially |
| `LocationAliases` | Exact accepted codes/names grouped by location slug |

Participating locations are the locations actually using this supplier account,
not every seeded dashboard operation. Nashville's fallback applies only while
that list contains Nashville alone and the reference contains no conflicting or
suspicious location signal. Before a second location uses the account, add it to
`ParticipatingLocationSlugs`; the fallback then stops automatically, and missing
location references require review. New references to a location not yet participating
also require review rather than being assigned to Nashville. Unknown historical
customer-name references are allowed to use the present single-location default.

For hand-entered references, a full city name (for example, `MURFREESBORO | job | name`)
is easier to recognize than an arbitrary short code. Prefer supplier-maintained
Job/Ship-to selections or a purchase-order reference generated by the dashboard and
copied verbatim when the supplier supports it. Those structured mappings are future
extensions, not implemented automatic matches. Job numbers must always be interpreted
with the selected local operation; they are not globally unique. This implementation
assigns one purchasing location per invoice. References indicating multiple locations
must be reviewed rather than creating duplicate AP liabilities or silently splitting
amounts. Cross-location line allocations remain outside this change.

The AP feed includes posted `payingEntityCode`, `purchasingLocationId` and
`yourReference` fields, and the structured candidate/reviewed payload for pending
imports. Payer configuration and routing defaults are source defaults only; no
production configuration or remote worker was updated by this preparation.

- `POST /api/supplier-invoices/decks-docks`: HTTPS, `X-CCV-Supplier-Key`, multipart
  `document` PDF and `invoice` JSON matching `SupplierInvoicePayload`. Maximum PDF
  size 20 MB; metadata 512 KiB; up to 1,000 extracted lines. The response includes
  `importId`, `status`, `payableInvoiceId`, and `duplicate`.
- `GET /api/supplier-invoices/ap`: HTTPS, `X-CCV-AP-Key`, read-only change feed.
  Optional `afterChangedAt` (UTC timestamp) and `afterId` form a compound cursor.
  Save both returned cursor fields after consuming the page. Polling with the cursor
  also returns review/posting changes to previously seen imports. Pages have at most
  100 records. `payloadJson` contains the reviewed extraction if posted, otherwise
  the candidate extraction. Status distinguishes posted invoices from review,
  rejected, and duplicate documents; consumers must never treat a pending document
  as an approved AP invoice or post another liability for an existing AP ID.
- `GET /api/supplier-invoices/{id}/document`: original PDF, available only to an
  authenticated Administrator or the separate AP read credential. Credentials never
  appear in URLs. Document access and feed access are audit logged.

The read key cannot ingest, modify, approve or pay an invoice. Administrator approval
uses the authenticated Blazor application. Server endpoints enforce their own
permissions independently of navigation visibility. Machine endpoints are disabled
without configuration and refuse insecure transport except loopback development.
Deployment behind TLS termination must retain the application's approved forwarding
configuration so HTTPS is recognized; do not trust arbitrary forwarded headers.

## Delivery status

Source implementation with live supplier UI mapping. The owner performed sign-in;
the invoice list, paid view, side-panel action, PDF popup host and grid structure
were observed. No build, automated tests, worker execution, bulk invoice download,
dashboard invoice import, shared-database migration, dashboard production
configuration change, dashboard deployment, schedule activation, live message,
payment, commit or push was performed. Hermes installation/dependency provisioning
and paused schedule registration completed through the separately authorized
Hermes PaperClip setup. Its installer and operating notes live under
`C:\Users\serveradmin\Documents\ChatGPT\Hermes PaperClip\supplier-invoices`.

## Entity scope and controller handoff (November 3, 2026)

Nashville is a separately owned entity outside Ventures Group accounting control.
Its supplier imports are dashboard reporting only, never Ventures accounting exports.
Murfreesboro and Knoxville map to the Ventures entity. Future separately owned LLCs
require an explicit entity mapping; ownership percentages do not split invoice costs.

Purchase ownership is server-derived from the selected location and stored in the
reviewed import JSON, separately from its optional payer. Legacy pending imports
clear the unverified default payer when opened. Historical posted data is not rewritten.
No schema migration is needed. Unmapped locations block posting. Nashville is always
reporting-only, including when configuration mistakenly enables its entity for export.
All supplier AP postings require administrator review regardless of the legacy auto-post setting.

The existing read-only AP feed is a reporting feed. Controller consumers must use
`GET /api/supplier-invoices/ap?accountingOnly=true` with its existing read credential.
This returns only posted, policy-stamped invoices whose reviewed entity mapping still
matches current policy and whose non-Nashville entity has explicit opt-in through
`SupplierInvoices:AccountingExportEntityCodes`. The default is empty, so nothing is
eligible. Use the returned cursor even for empty filtered pages; continue until the
source cursor stops advancing. A posting key identifies each dashboard import.

This is a provider-neutral accounting-review candidate feed, not a ledger writer.
There is no Mercury submission, external posting receipt, retry worker, payment,
or controller approval endpoint in this phase. A future connector must persist
external receipt identifiers, enforce destination/entity idempotency and reconcile
successful postings before external activation. Candidate readiness is not proof
of accounting approval or external posting. Posted statuses are not fabricated.

Review corrections do not train or change parser rules automatically. Report parser
exceptions for explicit source changes; preserve original PDFs and extraction history.
No controller export, production setting change or remote deployment is activated
by this source implementation.

## Customer-name and reference-number job lookup

Lookup uses synchronized Housecall Pro jobs scoped to the purchasing operation.
Whole job-number tokens in Your Reference are validated against HCP jobs first.
Otherwise the entire normalized customer name is matched, ignoring case, punctuation
and extra spaces. No fuzzy match, job status filter or date filter is used.
A printed or previously selected Job is retained only when it validates in that
operation. Distinct matching numbers appear as a comma-separated reference list and
individual dropdown options. One match can fill automatically; multiple matches
require one selection. No combined list is saved as the AP job number. Posting
revalidates the single number against operation-scoped HCP data on the server.
Missing or unsynchronized jobs must be resolved before approval. Lookup runs on
new intake and when a pending review opens; the button repeats it after edits.
This uses synchronized dashboard data, not a fresh live API scan. Source sync time
is displayed. No historical posted invoice is backfilled. No schema change required.

Customer/job lookup ignores the first dash (hyphen, en dash or em dash) and all
following reference text. For example, `Hannah Wemhoener-TREX` looks up
`Hannah Wemhoener`. Numbers in the ignored suffix are not job candidates. The
original Your Reference value is retained in the immutable source payload. This rule also treats a dash
inside a hyphenated customer name as the start of an ignored suffix.

Leading and trailing whitespace is trimmed from the editable Your Reference value
before job lookup and approval, and after editing the reference field. The original
PDF and immutable original extraction are preserved.

When exact reference-number and full-name matching find no jobs, try an exact last
name component, then an exact first name component if the last name finds nothing.
All searches remain location-scoped. A single distinct job number is selected automatically, including partial-name
results. Multiple candidates require dropdown selection; an existing validated job
entry is preserved.
A fallback message identifies which name component matched. Common names can return
unrelated customers, so the reviewer must confirm customer identity in HCP.

Missing invoice due dates default to invoice date plus 30 calendar days, per the
supplier terms confirmed by the owner. Existing due dates are preserved. The default
applies on intake, extraction refresh, opening pending reviews, and approval.

## Unassigned job expenses

Reviewers can explicitly choose Job unknown / assign later with a required reason.
The posted payable uses an empty job string, never a fabricated job number. Other
posting checks remain required. The review payload stores the flag and explanation;
no schema migration is required. The Unassigned job expenses filter lists posted
supplier imports whose linked AP job is blank. Assignment uses same-location HCP
jobs and a serialized supplier transaction to update the existing payable and its
payload, recording actor/time in an append-only JobAssigned event. No amounts,
lines or catalog decisions are recreated. Assigned rows leave the queue on refresh.
Unassigned invoices remain excluded from accounting-review export candidates.

The manual unknown-job checkbox and reason entry are superseded by automatic
classification. A blank Your Reference or a completed location-scoped lookup with
no jobs generates its own deferred-assignment reason. A valid existing job is kept.
Multiple candidates still require one selection. Approval repeats the lookup on the
server so a supplied unknown-job flag cannot bypass matching. Deferred invoices post
to the existing unassigned queue without an extra reviewer acknowledgment.

## Address fallback and job descriptions

The review now uses complete paginated HCP API job reads scoped to the selected
operation credential, cached for five minutes. Incomplete or failed reads block the
lookup instead of being treated as no matches. After number/full/last/first-name
matching finds no jobs, compare the invoice delivery address against job addresses,
using the house number and street name before the street-type suffix; city, ZIP and
unit text are excluded. Matching is exact after punctuation/whitespace normalization.
Description/name/summary, customer and address appear beside job numbers; absent
source descriptions are explicitly labeled unavailable. The same labels are used
in the later-assignment queue. Multiple numbers require one selection; a single
number fills automatically. Same-street ambiguity remains subject to review.

Positioned parser v2 extracts the printed Delivery Address line. Pending originals
can be re-extracted without touching posted imports. A delivery-address input also
allows the reviewer to correct missing or wrapped address extraction before lookup.
This phase makes no new HCP writes and adds no database columns.

Positioned parser v3 reads both inline Delivery Address text and the legacy top
address block, keeping its extraction within that column. Pending originals can
be refreshed with preservation of review edits. The review list displays a total
Needs Review count independent of its 100-row display limit and active filter,
refreshed after successful review actions and manual refresh.

## Effective-dated packaging history

Catalog review includes Supplier packaging review: search a SKU, select the vendor
item, view history, and confirm unit, package quantity, effective date and supplier
evidence. History is append-only; confirmation requires administrator identity and
records an operational audit. Recorded legacy values are seeded as unverified, not
as proof that every package contains one item. A packaging change closes earlier
prices before its effective date; existing prices on/after that date require prior
resolution. No automatic per-item price conversion is performed.

Supplier invoice lines explicitly distinguish verified packaging with evidence from
unknown package quantity. A confirmed catalog version on the invoice date can resolve
unknown packaging. Otherwise the AP line retains unknown packaging and catalog price
approval stays pending; the payable amounts remain as printed. Confirmed conflicts
and purchase-unit conflicts still block posting. New AP lines snapshot SKU,
description, purchase unit, package quantity and confirmed status independently of
later catalog changes. Existing posted imported lines are backfilled only from an
unambiguous match to their saved reviewed payload; unknown quantities stay unknown.
Other historical lines remain unverified rather than receiving invented quantities.
Catalog price review requires a confirmed packaging version for its effective date.
A schema migration adds history and nullable invoice snapshots without changing
invoice amounts or replacing original documents.

Each/EA with package quantity 1 is recognized as confirmed single-item packaging
on intake, opening a pending review, extraction refresh and approval. The automatic
evidence explains the unit meaning. Box/Pack quantities are not inferred. Changing
an automatically recognized unit or quantity clears that automatic confirmation;
explicit supplier-verified quantities above one are not overwritten. Original PDFs,
original extractions and previously posted invoices are retained.

Explicit count markers in invoice descriptions (50ct, 50 count, 4 Pack, 4-pk,
Pack of 6) take precedence over generic Each=1. Dimensions, lengths and SKU digits
are not treated as package counts. Different competing counts remain unconfirmed.
Ordered quantity, purchase unit, unit price and line total are never multiplied or
rewritten by count recognition. Legacy unverified catalog counts do not block AP
posting or silently become confirmed; confirmed catalog conflicts still require
packaging review. Invoice snapshots retain the recognized count while catalog
price approval remains pending for unconfirmed catalog mappings.
The packaging table/identity sequence must share the existing VendorProducts owner;
the corrective ownership migration handles releases created by the migration admin.

Verified invoice packaging can differ in both unit label and count from an
unverified legacy catalog mapping (for example invoice Each/50ct versus legacy
Box/1). Such legacy differences do not block AP: invoice snapshots retain the
verified packaging, catalog price updates remain pending, and confirmed catalog
conflicts or inactive SKUs still block. No existing packaging history or prices
are overwritten merely to clear an invoice validation error.
