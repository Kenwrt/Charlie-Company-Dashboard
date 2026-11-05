"""One daily Hermes request, leased to the WrightServer signed-in Chrome worker.

No browser/session/credential access. Receipt is committed only after a matching
immutable original has been archived on AI01. No dashboard or accounting writes.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import uuid
from zoneinfo import ZoneInfo
from home_depot_history import atomic, private_directory, single_run

ROOT = Path('/srv/ai/hermes/home-depot-history')
ZONE = ZoneInfo('America/Chicago')
REASONS = ('ChromeUnavailable', 'NeedsLogin', 'OwnerMfaRequired', 'AccessDenied',
           'DownloadFailed', 'CoverageUncertain', 'WorkerInterrupted')


def write(path, value):
    atomic(path, json.dumps(value, separators=(',', ':')).encode('utf-8'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('enqueue', 'claim', 'complete', 'blocked', 'status'))
    parser.add_argument('--run-id')
    parser.add_argument('--sha256')
    parser.add_argument('--range-label', default='')
    parser.add_argument('--reason', choices=REASONS)
    args = parser.parse_args()
    os.umask(0o077)
    state = private_directory(ROOT / 'state')
    queue = private_directory(state / 'owner-chrome-requests')
    now = dt.datetime.now(dt.timezone.utc)
    day = now.astimezone(ZONE).date().isoformat()
    with single_run(state):
        path = queue / (day + '.json')
        if args.run_id:
            run_id = str(uuid.UUID(args.run_id))
            matches = [p for p in queue.glob('*.json') if json.loads(p.read_text())['runId'] == run_id]
            if len(matches) != 1:
                raise RuntimeError('Daily request not found')
            path = matches[0]
        current = json.loads(path.read_text()) if path.exists() else None
        if args.action in ('enqueue', 'claim') and current is None:
            current = {'runId': str(uuid.uuid4()), 'localDate': day, 'timezone': str(ZONE),
                       'status': 'Pending', 'createdAtUtc': now.isoformat(), 'route': 'WrightServerOwnerChrome',
                       'completeHistory': False, 'dashboardHandoff': 'Deferred'}
            write(path, current)
        if args.action == 'claim':
            if current['status'] in ('Archived', 'NeedsOwnerAction'):
                print(json.dumps({'claimAcquired': False, 'request': current}))
                return
            lease = current.get('claimedAtUtc')
            if current['status'] == 'Collecting' and lease and now - dt.datetime.fromisoformat(lease) < dt.timedelta(hours=2):
                print(json.dumps({'claimAcquired': False, 'request': current}))
                return
            current.update(status='Collecting', claimedAtUtc=now.isoformat())
            write(path, current)
            print(json.dumps({'claimAcquired': True, 'request': current}))
            return
        if args.action == 'complete':
            if current is None or current['status'] not in ('Collecting', 'Archived'):
                raise RuntimeError('An acquired request is required')
            if not args.sha256 or not re.fullmatch('[a-fA-F0-9]{64}', args.sha256):
                raise RuntimeError('Original SHA-256 required')
            digest = args.sha256.lower()
            original = ROOT / 'inbox' / (digest + '.csv')
            if not original.is_file() or hashlib.sha256(original.read_bytes()).hexdigest() != digest:
                raise RuntimeError('Archived original missing or integrity mismatch')
            if current['status'] == 'Archived' and current.get('sha256') != digest:
                raise RuntimeError('This daily request already has a different original')
            if not args.range_label.strip() or len(args.range_label) > 160:
                raise RuntimeError('Observed non-private range label required')
            current.update(status='Archived', sha256=digest, observedRange=args.range_label,
                           archivedAtUtc=now.isoformat(), unavailableEvidence=['Returns', 'Cancellations', 'PaymentStatus', 'ReceiptTotals'],
                           completeHistory=False, dashboardHandoff='Deferred')
            write(path, current)
            write(state / 'owner-chrome-latest.json', current)
        elif args.action == 'blocked':
            if current is None or not args.reason or current['status'] == 'Archived':
                raise RuntimeError('Pending request and bounded failure reason required')
            current.update(status='NeedsOwnerAction', reason=args.reason, changedAtUtc=now.isoformat())
            write(path, current)
            write(state / 'owner-chrome-latest.json', current)
        print(json.dumps({'request': current, 'browserLaunched': False, 'credentialsAccessed': False}))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print(json.dumps({'status': 'DispatchIncomplete', 'completeHistory': False}))
        raise SystemExit(1)
