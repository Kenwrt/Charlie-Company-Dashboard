# Home Depot Analyst Windows collector

The Windows tasks replace the paused Codex personal-Chrome automation. The daily
task is CharlieCompany-HomeDepotAnalyst, 07:00 Central Standard Time (Central local
time including daylight saving). The on-demand task with suffix -Verification
uses a new headless Chrome context and requires fresh login before downloading.
Neither needs an open personal browser or Codex chat. Hermes retains its existing
daily request dispatcher; the worker uses that queue and the existing archive.

The runtime belongs to WrightServer\serveradmin at
C:\Users\serveradmin\AppData\Local\CharlieCompany\HomeDepotAnalyst.
An on-demand deployment task installs each compressed source artifact separately,
avoiding Windows command-line length limits. Task definitions never carry credentials.
The bootstrap runs deployed code inside the actual batch session and records a bounded
receipt in verification-bootstrap.json or daily-bootstrap.json.

Credentials are held in the user-scoped Windows Credential Manager generic target
CharlieCompany.HomeDepotAnalyst with machine-local persistence across user sessions.
Only that app-owned target is accessed. Set-WindowsCollectorCredential.ps1 provides
masked entry and a password-free -CheckOnly mode. The legacy XML credential file is
not the unattended worker's credential store. The installer can use an existing
protected XML credential for task registration only when the owner expressly confirms
it matches the Windows password and selects -UseConfirmedSharedPassword. Passwords
never appear in task definitions, CLI arguments, environment variables or logs.

Confirmed November 4, 2026: the batch task runs as serveradmin in Windows session 0,
reads its vault credential, imports its browser dependencies and reaches Home Depot's
email step. The supplier responds HTTP 403 with a temporary technical issue before
the password is submitted. This is not a successful login or download. An actual
desktop logoff test remains pending. Session 0 alone does not prove desktop logoff.

auth-access-block.json stops repeated authentication attempts after the observed
supplier denial. Do not remove it until the supplier access issue is resolved and
a fresh normal login test is authorized. No proxies, stealth settings, CAPTCHA
bypasses, copied personal-browser sessions or weakened Windows protections are used.

The archive's exact-file hash and daily request lease prevent duplicate evidence
handoffs; transaction-level dashboard ingestion remains deferred. Existing CSV replay
was verified earlier with zero new originals and three total unique originals.
Do not call a blocked login an end-to-end success or imply returns/cancellations are
covered by the settled-transaction CSV export.

## Controlled sandbox and archive checks, November 4, 2026

At 19:42 Central the explicit sandbox diagnostic ran in session 0 with a new isolated
temporary profile and chromium_sandbox=True. It again received HTTP 403 at the email
step, before password submission. The working ordinary Chrome window/profile was not
opened, modified, copied or closed by this test. No automation-hiding flags were used.

At 19:43 Central the separate archive-only verification passed in session 0 using a
previously archived original: zero new originals, zero replay originals, three unique
originals total. The original's SHA-256 was checked before and after private archiving.
The native SSH route and archive helper work in the batch session. The helper now uses
the same PowerShell 7 host as the launcher, correcting a Windows PowerShell mismatch.
This was not a fresh Home Depot download and is not proof of desktop logoff.

SandboxDiagnostic and ArchiveReplayOnly are explicit on-demand verification modes;
neither changes daily collection. The default verification task honors the supplier
denial marker. The daily 07:00 Central schedule remains registered, with the Codex
personal-browser schedule paused. The supplier authentication rejection remains the
blocker to fresh download and end-to-end acceptance.
