# Supplier invoice deployment, November 2, 2026

Dashboard release: `20261102-supplier-invoices-review-224818` on API01.
Retained rollback release: `20261029-9c55ad8`.

The backup failure was a version mismatch: API01 had pg_dump 16.15 while the
database was PostgreSQL 17.10. The existing PostgreSQL 17 database container created
a custom-format backup of the application database. Its archive listing succeeded;
the private transfer to API01 was SHA-256 checked against its receipt. No system
PostgreSQL package, database service, or monitoring policy was changed. Backup files
remain in private VM directories, not the Windows checkout.

`deploy/activate-supplier-release.py` now supports a recent, validated server-side
backup and reports client/server version mismatch distinctly. The deployment retained
the prior release and protected configuration backup, switched the current symlink
and restarted `charlie-company.service`. Both supplier-invoice migrations are applied.
The owner installed/reloaded the updated systemd unit before deployment.

Build/release publish succeeded. The .NET suite passed 34 tests, including migration
snapshot compatibility, location ambiguity, posting precision and TLS proxy trust. Nine synthetic
collector tests passed. An authenticated, invalid-document intake request returned
400 without creating an invoice, proving the machine credential and transport path.

The existing WEB01 TLS proxy is used directly over the private LAN, preserving the
public hostname, SNI and certificate validation. The supplier endpoints trust forwarded
HTTPS only from that exact proxy address. No Cloudflare settings, certificates,
firewalls, or public administration endpoints were changed. The general public route
returned 403 to the automation client; the authorized private proxy path avoids that
dependency without disabling TLS verification or application authentication.

Protected configuration assigns supplier ID 2, Nashville only, and payer `ventures`.
`SupplierInvoices__AutoPostVerifiedInvoices` is false. The collector received only its
ingestion key through an RSA-OAEP encrypted handoff; the independent AP read key stays
on API01. No credentials, customer account paths or signed document URLs are included
in source or this report.

Hermes worker source/dependencies are installed. The dedicated login profile uses
the existing system Chrome, whose sandbox starts successfully, rather than disabling
the sandbox for the downloaded Chromium binary. The owner completed the dedicated
Hermes sign-in. Temporary preview/tunnel processes were stopped afterward. The
earlier in-app supplier session was not copied or exported.

The first collection downloaded 46 PDFs. All 46 received durable dashboard receipts:
33 are Needs Review and 13 were detected as supplier invoices already in AP. Every
candidate is assigned to Nashville and payer `ventures`. No new AP invoices were
posted. Thirty-four documents have candidate extracted lines; all invoice numbers
were extracted. Incomplete/uncertain line extraction remains visible for review.
A second collection submitted zero documents, exercising duplicate suppression.

Two runtime fixes were required: allow only parsed QUERY operations to the observed
Suppli GraphQL endpoint (mutations and other write requests stay blocked), and retain
over-precision extraction in review rather than rejecting its original PDF. AP
posting still rejects unsupported precision; values are never silently rounded.

Job `2e5352d07606` is enabled and scheduled daily at 06:00 America/Chicago. Its saved
next run is November 3, 2026 at 06:00 CST. It is script-only, local-output-only, and
does not use model inference. The completed initial runs are direct worker runs;
the next unattended scheduled execution is not yet claimed. Automatic AP posting
remains disabled. Commit/push have not been performed.
