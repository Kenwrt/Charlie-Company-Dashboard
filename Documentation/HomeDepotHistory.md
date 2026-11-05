# Home Depot purchase-history intake

This source feature is disabled by default. No build, tests, deployment, shared-DB
migration, activation, commit/push, price update or live job assignment was performed.
The Windows collector/archive runtime is documented separately in
`Tools/HermesHomeDepotHistory/WINDOWS-COLLECTOR.md`; its existence does not mean the
CSV has been imported into a running dashboard.

## Authoritative purchasing policy

Home Depot purchases are prepaid credit-card material purchases, never accounts
payable. The source purchase model exposes PrepaidCreditCard, CreatesAccountsPayable
false and CardReconciliationStatus Deferred. This business policy is separate from
independent payment/settlement verification. Unknown settlement evidence is retained
as unknown; card balances and Mercury records are not fabricated or changed.

The generic supplier invoice posting service rejects Home Depot before creating
payables or modifying catalog prices. Existing historical financial records are
preserved. Decks & Docks keeps its existing invoice/AP workflow. Home Depot is not
routed through that workflow, and no unpaid Home Depot payable candidate is emitted.

## Job materials and common vocabulary

See `Documentation/JobMaterials.md` for the canonical model and matching rules.
Administrator pages:

- `/admin/home-depot-history`: immutable CSV/PDF intake, source review, candidate job
  lookup and line-level material attribution.
- `/admin/job-materials`: operation + job-number search across Home Depot and existing
  supplier invoice imports; supplier/SKU/reference crosswalk review.

Durable job identity is operation + Housecall Pro external job ID, not a customer's
name or an unqualified job number. Reference/date-range mappings and line allocations
require explicit review and operation-scoped HCP confirmation. Quantities/amounts in
split line allocations must exactly conserve the source. Material attribution does
not require a receipt total or payment evidence; its amount basis remains provisional.

The existing Home Depot supplier and VendorProduct/Product catalog records are
reused. Unique exact supplier SKU matches and reviewed SKU crosswalks link identity
only; they never update prices or overwrite historical source amounts. Package units
and actual installed consumption remain distinct from purchased quantities.

The dashboard uses familiar headings such as Purchase date, Supplier SKU, Item
description, Purchased quantity, Reported unit amount and Reported line amount. Raw
Home Depot values and source headers remain available for provenance. Lighthouse
Place/Pl -> QUINN is a display convention only, not an automatic HCP assignment.

## Private persistence and duplicate control

HomeDepotHistoryStore keeps SHA-256-addressed originals, raw values, per-line source
provenance, immutable revisions and append-oriented audit events. A file lock
serializes writers and version/revision checks reject stale edits. Storage must stay
outside the application/web root, with private service-account permissions. Originals,
current state and revisions must remain together in backup. No retention/deletion
operation is activated by this change.

Document replay is idempotent. Transaction identity uses account + date + store +
register + transaction ID, with a constrained invoice/date/store fallback. Missing
identifiers remain unresolved records; order numbers alone never establish identity.
Identical lines are occurrence-counted, not discarded. Overlapping exports contribute
provenance rather than another expense copy. Changed snapshots create conflicts and
require review. Reporting selects one CSV snapshot per transaction; alternatives are
not added together. Material assignments that no longer conserve a reporting line's
quantity/amount are excluded and flagged for re-review.

All source rows survive, including missing job references, negative amounts and
undated rows. Excluded is a reversible review status, not deletion of evidence.

## APIs and protected activation settings

- `POST /api/home-depot-history/intake`: one bounded original, Csv or supported PDF
  evidence type. Uses HTTPS/trusted transport plus X-CCV-HomeDepot-Key.
- `GET /api/home-depot-history/materials`: private source snapshot for authorized
  Administrator or protected read-key clients. No AP eligibility or external posting.
- `GET /api/home-depot-history/ap`: authorized callers receive HTTP 410; Home Depot
  is not an AP source.
- `GET /api/home-depot-history/documents/{id}`: authorized original download with
  private/no-store headers and access audit.

Existing protected setting names remain HomeDepotHistory:Enabled, AccountScope,
StoragePath, IngestionKey, AccountingReadKey (legacy read-key name),
TrustedProxyAddresses and OperationCompanyIds. This source change does not set or
expose their values. Company/payer mappings never fall back to an inferred location.
Read snapshots are audited; the feed is a full snapshot, not a change stream.

The November 5 original is now imported into the API01 manual reviewer with all
2,772 source rows retained. HCP matches are proposals requiring review. The local
job-account artifact remains separate from accounting. Supplier CSV coverage is not
proof of receipt totals, complete returns/cancellations or paid-card reconciliation.
