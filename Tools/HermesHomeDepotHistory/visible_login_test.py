"""Owner-operated visible version of the collector's fresh Chrome context.

Playwright's isolated temporary profile is used; personal profiles are never opened
or copied. No passwords, DOM snapshots, cookies, headers or response bodies are read.
"""
import datetime as dt
import json
from pathlib import Path
import time
from urllib.parse import urlparse
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent
STATUS = ROOT/'visible-login-test.json'
state = {'mode': 'VisibleManualLogin', 'profile': 'DedicatedTemporary',
         'credentialsRead': False, 'authStatuses': []}


def record(stage):
    state.update(stage=stage, updatedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat())
    STATUS.write_text(json.dumps(state), encoding='utf-8')


try:
    with sync_playwright() as pw:
        # Same Chrome channel, launch defaults and context options as the collector;
        # only headless=False changes. No fingerprint/stealth or security overrides.
        browser = pw.chromium.launch(channel='chrome', headless=False, chromium_sandbox=True)
        context = browser.new_context(accept_downloads=True)
        page = context.new_page()

        def response_seen(response):
            parsed = urlparse(response.url)
            if parsed.hostname == 'www.homedepot.com' and parsed.path == '/customer/auth/v1/twostep/init':
                state['authStatuses'].append(response.status)
                record('OwnerLoginResponseObserved')

        page.on('response', response_seen)
        page.goto('https://www.homedepot.com/', wait_until='domcontentloaded', timeout=60000)
        record('VisibleBrowserReady')
        try:
            page.get_by_role('button', name='open drawer to view Account', exact=True).click(timeout=15000)
            page.get_by_role('link', name='Sign in', exact=True).click(timeout=15000)
            record('ManualSignInReady')
        except Exception:
            record('VisibleBrowserReadyForOwnerNavigation')
        # Owner controls the normal browser. Closing it ends this temporary test.
        deadline = time.monotonic()+3600
        while browser.is_connected() and context.pages and time.monotonic() < deadline:
            try:
                page.wait_for_timeout(1000)
            except Exception:
                break
        record('VisibleTestEnded')
        browser.close()
except Exception as failure:
    state['exceptionType'] = type(failure).__name__
    record('VisibleTestIncomplete')
    raise SystemExit(1)
