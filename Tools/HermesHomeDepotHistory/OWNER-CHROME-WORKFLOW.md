# Home Depot Analyst local browser worker

This is the local browser half of the existing Hermes agent, not a second collector.
Hermes job `5a5d08b686c3` dispatches daily at 07:00 America/Chicago. The local scheduled
task in this existing Codex chat runs at the same time, takes one date-scoped lease,
and controls the owner's already signed-in Chrome through the approved browser tool.
The lease protocol removes ordering/race dependence between the two scheduler ticks.

Keep WrightServer, its signed-in desktop, Chrome and the Codex desktop app running.
The Chrome browser connector must remain enabled. If any are unavailable, record a
blocked run and ask only for the specific required owner action. Do not launch a fresh
browser, open a public debugging port, copy a profile/cookie database, read Gmail codes,
or fall back to AI01's rejected session. Account changes, billing, passwords and
authentication challenges remain outside browser-worker authority.

## Each daily run

1. Claim the daily request using the existing private SSH identity:

   ```powershell
   ssh -o BatchMode=yes -o ConnectTimeout=15 ken@10.168.168.161 '/srv/ai/hermes/home-depot-history/venv/bin/python /srv/ai/hermes/home-depot-history/owner_chrome_dispatch.py claim'
   ```

   Claim creates today's request if Hermes hasn't queued it yet. If `claimAcquired`
   is false, do not download. `Archived` is already done; `Collecting` means another
   worker owns a live lease; `NeedsOwnerAction` must not be silently retried. Preserve
   the returned `runId`. Never print private state beyond the bounded dispatch result.

2. Use the computer-use skill and the Chrome browser connector on WrightServer.
   Reuse the owner's existing authenticated Home Depot tab/profile. Prefer an existing
   purchase-history tab. The currently authorized Chrome profile is the owner's Ken
   profile; browser/tab IDs can change, so select by the actual inventory rather than
   trusting stale IDs. Do not inspect email tabs or credential/session storage.

3. Open the normal Purchase History link. If login/MFA/CAPTCHA is needed or access is
   denied, do not request/solve codes. Record `NeedsLogin`, `OwnerMfaRequired`, or
   `AccessDenied` as appropriate using step 7. Chrome being available does not prove
   authentication or a successful source read.

4. Read Date Placed and its actual supported ranges. Export the broadest available
   range, with no invented start date; record the exact non-private range label.
   The observed default was `Last 25 Months`. An export follows the selected date
   range: the portal does not necessarily force an entire lifetime export.

5. Click **Export**, choose **Details Data** in the export dialog, and use a unique
   output filename `HomeDepotAnalyst-<local-date>-<runId>.csv` if the filename field is
   available. Submit the export once. The confirmed UI success text is
   `Purchase History Data successfully exported. Please check your downloads folder for the file.`
   The browser download-event API previously timed out even when the CSV downloaded;
   do not confuse that tool timeout with a failed site export. Use the UI message
   plus the actual unique file in `C:\Users\serveradmin\Downloads`. If the portal
   instead generates asynchronously, use its normal Downloads page and visible
   controls. Limits, interrupted reads and unknown ranges stay explicit. No fake
   success or inferred row/financial totals. Do not click QuickBooks Exports, Add
   Receipt, payment, order, account or PO/job edit controls.

6. Publish only the actual original and acquired request:

   ```powershell
   & 'C:\Dev\CharlieCompanyActiveDashboard\Tools\HermesHomeDepotHistory\Publish-OwnerChromeExport.ps1' -SourcePath '<actual downloaded CSV path>' -RunId '<returned runId>' -RangeLabel '<observed range label>'
   ```

   This copies the source through existing SSH, checks its type/size/SHA-256, archives
   it privately, and commits the daily receipt. Exact replay is safe. An `Archived`
   receipt is required before claiming success. Do not run dashboard intake or
   accounting exports. A changed full export is retained as new evidence; unchanged
   originals are archived once. The dashboard, when separately activated, owns
   transaction and repeated-line deduplication. Never remove source rows yourself.

7. If collection blocks, record a bounded reason, never a raw private error body:

   ```text
   ssh ... '/srv/ai/hermes/home-depot-history/venv/bin/python /srv/ai/hermes/home-depot-history/owner_chrome_dispatch.py blocked --run-id <runId> --reason <reason>'
   ```

   Allowed reasons: `ChromeUnavailable`, `NeedsLogin`, `OwnerMfaRequired`, `AccessDenied`,
   `DownloadFailed`, `CoverageUncertain`, `WorkerInterrupted`. Report the specific
   owner action when necessary; otherwise preserve queued evidence for safe recovery.

8. Leave the owner's existing browser/tabs/session available. Output only operational
   result, observed source range, archive counts/receipt, and material blockers.
   CSVs contain settled transactions and omit returns and cancellations. Never call
   this complete receipt/payment history. Download, archive, dashboard processing,
   deployment and Mercury posting are distinct states; the last three remain deferred.

## Boundaries

No browser-cookie/session/credential copying, saved password extraction, email-code
reading, authentication/security bypass, purchases, payments, returns, Home Depot
edits, catalog updates, HCP matching, shared migrations, dashboard deployment, Git
pushes or Mercury writes. No automatic retries of supplier sign-in failures. No
duplicate agents or independent collection schedules. Routine notifications are
limited to meaningful new data, completion/failure or required owner action.

## Authorized password-manager sign-in

The owner authorizes using their Home Depot username and existing password through
Chrome's built-in password manager in the existing Ken profile. The owner enters
and saves the password directly in Chrome; never receive it through chat, scripts,
configuration, environment variables, logs or SSH. Do not reveal, read, copy or
inspect password values, password-manager records, cookies or browser profiles.
If signed out, use the normal Home Depot sign-in UI with ken@charliecompany.pro,
select the password method if offered, and let Chrome autofill the saved password.
Do not capture accessibility snapshots, DOM values or screenshots of password fields
after autofill. Submit the normal Sign In control without reading the field value,
then observe the resulting non-credential page. Make at most one sign-in attempt.
If autofill is absent, Chrome requires owner unlock, or MFA/CAPTCHA/access denial
appears, stop at that boundary and record NeedsLogin, OwnerMfaRequired or AccessDenied.
Never reset/change credentials, read email codes or weaken security. This section
supersedes earlier instructions to stop merely because the existing session signed
out; all other collection, claim/receipt and duplicate-protection rules still apply.
The daily schedule remains 07:00 America/Chicago. Chrome/Codex/connector and the
signed-in Windows desktop must be available. Password autofill readiness requires
owner setup and a successful controlled sign-in before it can be called verified.
