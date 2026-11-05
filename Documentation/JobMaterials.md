# Purchased materials by job

Source implementation for the existing dashboard. No build, tests, activation,
deployment, database migration, Git commit/push or live data assignment was performed.

## Business boundary

Home Depot purchases are prepaid credit-card material purchases. They never create
accounts payable. The existing supplier-invoice posting service rejects Home Depot
before creating a payable or changing catalog items/prices. Existing historical AP
records are not deleted or rewritten. Settlement evidence remains distinct from the
owner's prepaid purchasing policy; the policy does not fabricate payment verification.
Credit-card/Mercury reconciliation is deferred to a separately authorized future agent.

Decks & Docks retains its invoice, review, catalog and payment workflow. Its material
lines can be reported without making Home Depot follow that workflow.

## Canonical identity

Job lookup at `/admin/job-materials` requires an operation and exact job number.
The report resolves one synchronized Housecall Pro job and uses operation ID + HCP
external job ID as the durable identity. A duplicate job number inside an operation
requires resolution; the first match is never silently selected. The source job sync
timestamp is displayed. Saving an attribution additionally confirms the actual ID and
number through the operation-scoped HCP API; unavailable or incomplete lookup blocks
the assignment rather than becoming a successful no-match.

Home Depot Job Name/Project Name and supplier PO/reference text remain source data.
Display aliases such as Lighthouse → QUINN do not establish HCP identity. Names and
abbreviations can suggest candidates, but are not unique keys.

### Home Depot source-reference lookup

The Home Depot review's Find HCP candidates action uses the current reporting CSV
snapshot's raw Job Name and Project Name, plus optional reviewer-entered reference
and address. It calls the same operation-scoped, paginated GetSupplierJobsAsync API
used by Decks & Docks. The supplier-specific Decks & Docks suffix stripping is not
applied to Home Depot references or addresses.

Candidates are ranked by evidence: whole job number, normalized street address,
exact homeowner name, then weaker address/name-component matches. The matcher
recognizes common street suffix/directional abbreviations, retains house numbers,
and checks apartment/unit information. Conflicting street suffixes or unit numbers
do not become exact-address candidates. Missing unit/suffix or unconfirmed city/ZIP
remains explicit; same-address repeat jobs still need project review.

A reference resembling a street address is not matched to an HCP job merely because
the house number equals a job number. For example, 1236 LIGHTHOUSE PL is address
evidence, not automatic job 1236. Its raw address is used rather than the QUINN
display alias. Whole name components are compared rather than substrings inside
names. Multiple projects for the same homeowner remain separate candidates.

A unique strongest candidate may prefill a suggested job in the review controls;
it does not create a reference mapping or line allocation. The reviewer sees the
source reference, actual job number, homeowner, address, description, operation
and match explanation. Saving attribution separately confirms the operation/job
identity. Candidates from unavailable/incomplete API reads are cleared and never
reported as a successful no-match. Generic review JSON cannot forge lookup results.

Lookup context is preserved as lookup evidence, separately from the purchasing
operation or financial review. Reopening after lookup refreshes the proposed jobs
in the UI without silently assigning the transaction.

## Home Depot attribution

The existing private HomeDepotHistoryState stores additive, audited material links:

- One explicitly selected existing Home Depot SupplyVendor ID. No duplicate supplier
  is automatically created.
- Reviewed source-reference + inclusive date range → operation/HCP job ID links.
  Overlapping reference ranges are rejected, including ranges across operations.
  Reused customer names must be separated by dates or overridden at the line level.
- Reviewed line allocations, with source line ID, operation, job ID/number, quantity
  and reported source amount. Whole-line assignment is exposed in purchase review.
  The service also supports explicit split allocations that conserve the exact source
  quantity and amount, retain its sign and identify distinct jobs. A split-entry UI
  is not yet supplied; no guessed percentage split is performed.
- Reviewed source-SKU → existing VendorProduct ID crosswalks under the selected
  supplier. Existing canonical Product IDs connect catalog identity across suppliers.

Line allocations take precedence over reference/date mappings. Undated lines cannot
use a date-range mapping. A later source snapshot that changes quantities/amounts
invalidates inconsistent allocations for reporting; originals and audit history remain.
Generic financial-review JSON cannot override validated material allocations.

Material attribution does not require a verified card charge or receipt total. It
uses clearly labeled CSV source amounts; it never converts these into paid balances.

## Common vocabulary

| Home Depot source | Reporting vocabulary |
| --- | --- |
| Job Name / Project Name | Source PO/job reference |
| SKU Number | Supplier SKU |
| Internet SKU | Home Depot internet SKU (separate identifier) |
| SKU Description | Item description |
| Quantity | Purchased quantity in source purchase units |
| Net Unit Price | Reported unit amount; not a validated paid-net amount |
| Extended Retail (before discount) | Reported line amount before discount |
| Transaction / register / store / date | Source receipt identity, retained for provenance |

Unique exact SKU matches within the selected supplier resolve read-only catalog
candidates; conflicting SKU/Internet-SKU matches require review. Manual crosswalks
support different catalog identifiers. Descriptions, catalog retail prices and loose
SKU similarities never replace historical purchase amounts. Package conversion is
not inferred: source purchase quantities are not summed as physical units across
different SKUs or suppliers until unit/packaging semantics are confirmed.

## Combined report

The Administrator-only report contains supplier, SKU, description, purchase date,
quantity/unit, source amount/basis, catalog link, assignment explanation, source
record/line/document IDs and review status. Public service operations also enforce
Administrator authorization. Unmatched and missing-source coverage is explicit.

Current sources are Home Depot history and existing supplier invoice imports,
including Decks & Docks and other suppliers already using that intake. Supplier
imports are projected once; their AP invoices are not added as another expense.
Rejected/duplicate imports are omitted. Multi-job supplier documents without line
allocations are excluded and counted as a coverage gap rather than copied in full
to every job. Provisional extraction is visibly flagged; missing sources do not
establish zero purchases. Additional supplier systems require their own source adapter.

Subtotals remain separate by supplier, currency and amount basis. Home Depot source
retail and supplier invoice line costs do not masquerade as one settled-cost figure.
Document-level tax/freight and card settlements are not silently allocated. This is
a purchased/allocated materials report, not an installed-consumption or inventory ledger.

## Activation and data state

The November 5 CSV review is a separate private artifact and archived original. It
has not been imported into a live dashboard by these changes. Home Depot intake
remains disabled by default and requires separately authorized configuration and
deployment. New links use the existing private versioned file store, so no shared
database migration is introduced. No actual supplier, SKU, or HCP job mapping is
claimed to have been applied until an Administrator reviews and saves it on an
activated instance. Prices, catalog membership and Mercury remain unchanged.
