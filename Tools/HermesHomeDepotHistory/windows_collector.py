"""Dedicated unattended browser. Credentials arrive only over a private stdin pipe.

No personal Chrome profile, tracing, screenshots, console capture or raw exceptions.
No dashboard ingestion. The existing Hermes lease and archive receipt remain authoritative.
"""
import json
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parent
REMOTE = '/srv/ai/hermes/home-depot-history'
STAGE = 'Startup'
AUTH_HTTP_STATUS = None
AUTH_BLOCK = ROOT/'auth-access-block.json'
LOGOFF_MONITOR = None


def dispatch(action, *args):
    result = subprocess.run(['ssh', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15',
                             'ken@10.168.168.161',
                             ' '.join([REMOTE+'/venv/bin/python', REMOTE+'/owner_chrome_dispatch.py', action, *args])],
                            capture_output=True, text=True, timeout=60)
    if result.returncode:
        raise RuntimeError('DispatchUnavailable')
    return json.loads(result.stdout)


def visible(locator):
    return locator.count() > 0 and locator.first.is_visible()


def collect(email, password, destination):
    global STAGE, AUTH_HTTP_STATUS
    controlled_sandbox_test = '--verify' in sys.argv and '--sandbox-diagnostic' in sys.argv
    reuse_profile = '--reuse-dedicated-profile' in sys.argv
    if AUTH_BLOCK.exists() and not controlled_sandbox_test and not reuse_profile:
        STAGE = 'SupplierAccessDenied'
        AUTH_HTTP_STATUS = 403
        raise RuntimeError('SupplierAccessBlocked')
    STAGE = 'BrowserLaunch'
    from playwright.sync_api import sync_playwright
    with sync_playwright() as pw:
        if reuse_profile:
            profile = ROOT/'manual-control-profile'
            if not profile.is_dir():
                STAGE = 'DedicatedProfileMissing'
                raise RuntimeError('DedicatedProfileMissing')
            # Open the exact owner-authorized dedicated profile in place. Chrome
            # handles its encrypted session; do not read/copy/export cookies or keys.
            context = pw.chromium.launch_persistent_context(str(profile), channel='chrome',
                      headless=True, chromium_sandbox=True, accept_downloads=True)
            browser = None
        else:
            browser = pw.chromium.launch(channel='chrome', headless=True, chromium_sandbox=True)
            context = browser.new_context(accept_downloads=True)
        page = context.new_page()
        def observe_auth_status(response):
            global AUTH_HTTP_STATUS
            parsed = urlparse(response.url)
            if parsed.hostname == 'www.homedepot.com' and parsed.path == '/customer/auth/v1/twostep/init':
                AUTH_HTTP_STATUS = response.status
        page.on('response', observe_auth_status)
        page.set_default_timeout(30000)
        if not reuse_profile:
            STAGE = 'HomePage'
            page.goto('https://www.homedepot.com/', wait_until='domcontentloaded', timeout=60000)
            STAGE = 'AccountMenu'
            page.get_by_role('button', name='open drawer to view Account', exact=True).click()
            STAGE = 'SignInLink'
            page.get_by_role('link', name='Sign in', exact=True).click()
            # Normal supplier login only. Do not mask automation or suppress challenges.
            STAGE = 'EmailEntry'
            # Home Depot exposes its email input as a textbox, not consistently type=email.
            email_box = page.get_by_role('textbox').filter(visible=True)
            email_box.first.wait_for(state='visible', timeout=60000)
            if email_box.count() != 1:
                raise RuntimeError('AmbiguousEmailForm')
            email_box.fill(email)
            STAGE = 'EmailContinue'
            page.get_by_role('button', name='Continue', exact=True).click()
            STAGE = 'PasswordOption'
            choice = page.get_by_role('button', name='Sign In with a Password', exact=True)
            try:
                deadline = time.monotonic()+60
                while not visible(choice):
                    if AUTH_HTTP_STATUS == 403:
                        STAGE = 'SupplierAccessDenied'
                        raise RuntimeError('SupplierAccessDenied')
                    if time.monotonic() >= deadline:
                        raise TimeoutError('Password option not available')
                    page.wait_for_timeout(250)
            except Exception:
                if visible(page.get_by_text('temporary technical issue', exact=False)):
                    STAGE = 'SupplierTechnicalIssue'
                elif visible(page.get_by_text('Access Denied', exact=False)):
                    STAGE = 'SupplierAccessDenied'
                raise
            choice.click()
            STAGE = 'PasswordEntry'
            page.locator('input[type="password"]').fill(password)
            STAGE = 'PasswordSubmit'
            page.get_by_role('button', name='Sign In', exact=True).click()
            # Auth success must precede navigation/export; never interpret submission as success.
            STAGE = 'AuthenticatedAccount'
            page.get_by_role('button', name='open drawer to view Account', exact=True).wait_for(timeout=60000)
        STAGE = 'PurchaseHistory'
        page.goto('https://www.homedepot.com/myaccount/purchase-history', wait_until='domcontentloaded')
        date = page.get_by_role('button', name='Date Placed', exact=True)
        if reuse_profile:
            STAGE = 'SavedSessionCheck'
        date.wait_for()
        label = date.inner_text().strip()
        # Only the previously observed supported range is accepted, not guessed coverage.
        if 'Last 25 Months' not in label:
            raise RuntimeError('CoverageUncertain')
        STAGE = 'ExportDialog'
        page.get_by_role('button', name='Export', exact=True).click()
        page.get_by_role('checkbox', name='Details Data', exact=True).check()
        # The dialog filename field is the visible CSV-name textbox, never a source path.
        name_field = page.get_by_role('textbox').filter(visible=True)
        # Purchase search may also be visible: identify filename using its .csv value.
        candidates = [field for field in name_field.all() if field.input_value().lower().endswith('.csv')]
        if len(candidates) != 1:
            raise RuntimeError('DownloadFailed')
        candidates[0].fill(destination.name)
        exports = page.get_by_role('button', name='Export', exact=True)
        if exports.count() != 2:
            raise RuntimeError('DownloadFailed')
        STAGE = 'Download'
        with page.expect_download(timeout=120000) as download:
            exports.last.click()
        download.value.save_as(str(destination))
        page.get_by_text('Purchase History Data successfully exported.', exact=False).wait_for()
        context.close()
        if browser:
            browser.close()
    data = destination.read_bytes()
    if not 0 < len(data) <= 20*1024*1024 or b'Date,Store Number,' not in data[:65536]:
        raise RuntimeError('DownloadFailed')
    return 'Last 25 Months'


def main():
    global STAGE, LOGOFF_MONITOR
    credentials = json.load(sys.stdin)
    email = credentials.pop('username')
    password = credentials.pop('password')
    if '--wait-for-logoff' in sys.argv:
        STAGE = 'WaitingForWindowsSignOut'
        from windows_sessions import wait_for_logoff
        LOGOFF_MONITOR = wait_for_logoff(ROOT)
    if '--verify-archive-only' in sys.argv:
        password = None
        STAGE = 'ArchiveSourceTransfer'
        # Previously verified original, not a new supplier export or an inferred result.
        digest = 'c5e14657dd22f0cf5ecdad71bc41e1131a812fb291bd753f496c74b7477942d8'
        downloads = ROOT/'downloads'
        downloads.mkdir(exist_ok=True)
        target = downloads/('archive-replay-'+digest+'.csv')
        copied = subprocess.run(['scp','-q','-o','BatchMode=yes','-o','ConnectTimeout=15',
                                 '-o','StrictHostKeyChecking=yes',
                                 'ken@10.168.168.161:'+REMOTE+'/inbox/'+digest+'.csv',str(target)],
                                capture_output=True,text=True,timeout=60)
        if copied.returncode:
            raise RuntimeError('ArchiveSourceUnavailable')
        import hashlib
        if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
            raise RuntimeError('ArchiveIntegrityMismatch')
        STAGE = 'ArchiveReplay'
        result = subprocess.run([r'C:\Program Files\PowerShell\7\pwsh.exe','-NoProfile','-NonInteractive','-File',
                                 str(ROOT/'Verify-ArchiveReplay.ps1'),'-SourcePath',str(target)],
                                capture_output=True,text=True,timeout=240)
        if result.returncode:
            raise RuntimeError('ArchiveReplayIncomplete')
        receipt = json.loads(result.stdout)
        receipt.update(source='PreviouslyArchivedOriginal',freshDownloadVerified=False)
        print(json.dumps(receipt))
        return
    if '--verify' in sys.argv[1:]:
        downloads = ROOT/'downloads'
        downloads.mkdir(exist_ok=True)
        target = downloads/('HomeDepotAnalyst-verification-'+str(uuid.uuid4())+'.csv')
        observed_range = collect(email, password, target)
        password = None
        result = subprocess.run([r'C:\Program Files\PowerShell\7\pwsh.exe', '-NoProfile', '-NonInteractive', '-File',
                                 str(ROOT/'Verify-ArchiveReplay.ps1'), '-SourcePath', str(target)],
                                capture_output=True, text=True, timeout=240)
        if result.returncode:
            raise RuntimeError('ArchiveVerificationIncomplete')
        receipt = json.loads(result.stdout)
        receipt.update(freshBrowserLogin='--reuse-dedicated-profile' not in sys.argv,
                       savedSessionReused='--reuse-dedicated-profile' in sys.argv,
                       observedRange=observed_range)
        if LOGOFF_MONITOR:
            receipt.update(LOGOFF_MONITOR.finish())
        # Batch-session execution is evidence distinct from actual desktop logoff.
        import ctypes
        session = ctypes.c_ulong()
        if not ctypes.windll.kernel32.ProcessIdToSessionId(ctypes.windll.kernel32.GetCurrentProcessId(), ctypes.byref(session)):
            raise RuntimeError('SessionEvidenceUnavailable')
        receipt['windowsSessionId'] = session.value
        print(json.dumps(receipt))
        return
    claim = dispatch('claim')
    if not claim['claimAcquired']:
        print(json.dumps({'status': 'Skipped', 'dailyStatus': claim['request']['status']}))
        return
    run = claim['request']['runId']
    if not re.fullmatch('[a-f0-9-]{36}', run):
        raise RuntimeError('DispatchUnavailable')
    reason = 'NeedsLogin'
    try:
        downloads = ROOT/'downloads'
        downloads.mkdir(exist_ok=True)
        target = downloads/('HomeDepotAnalyst-'+run+'.csv')
        range_label = collect(email, password, target)
        password = None
        reason = 'DownloadFailed'
        result = subprocess.run([r'C:\Program Files\PowerShell\7\pwsh.exe', '-NoProfile', '-NonInteractive', '-File',
                                 str(ROOT/'Publish-OwnerChromeExport.ps1'), '-SourcePath', str(target),
                                 '-RunId', run, '-RangeLabel', range_label],
                                capture_output=True, text=True, timeout=240)
        if result.returncode:
            raise RuntimeError('DownloadFailed')
        receipt = json.loads(result.stdout)
        if receipt['request']['status'] != 'Archived':
            raise RuntimeError('DownloadFailed')
        print(json.dumps({'status': 'Archived', 'runId': run, 'range': range_label,
                          'sha256': receipt['request']['sha256'], 'dashboardHandoff': 'Deferred'}))
    except Exception as failure:
        password = None
        if AUTH_HTTP_STATUS == 403:
            reason = 'AccessDenied'
        dispatch('blocked', '--run-id', run, '--reason', reason)
        raise RuntimeError(reason) from None


if __name__ == '__main__':
    try:
        main()
    except Exception as failure:
        if AUTH_HTTP_STATUS == 403:
            AUTH_BLOCK.write_text(json.dumps({'reason':'SupplierAccessDenied','httpStatus':403}), encoding='utf-8')
        # Never persist source exception text: browser calls may include sensitive values.
        print(json.dumps({'status': 'NeedsOwnerAction', 'reason': 'CollectorIncomplete',
                          'stage': STAGE, 'exceptionType': type(failure).__name__,
                          'authHttpStatus': AUTH_HTTP_STATUS, 'chromiumSandboxEnabled': True,
                          'browserProfile': 'OwnerDedicated' if '--reuse-dedicated-profile' in sys.argv else 'IsolatedTemporary',
                          'controlledSandboxTest': '--sandbox-diagnostic' in sys.argv,
                          **(LOGOFF_MONITOR.finish() if LOGOFF_MONITOR else {})}))
        sys.exit(1)
