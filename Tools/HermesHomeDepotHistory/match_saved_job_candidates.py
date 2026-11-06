"""Owner-invoked API01 maintenance; preview by default, --apply saves material links.

Uses existing candidates only, revalidates against complete live HCP jobs and the
report's synchronized job identity. Does not change customers, SKUs, AP or HCP.
Run on API01 as the application owner. Output contains aggregate counts only.
"""
import argparse
from collections import Counter
from decimal import Decimal, InvalidOperation
import fcntl
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import unicodedata
import urllib.request
import uuid
from datetime import datetime, timezone


def normalize(value):
    return ' '.join(''.join(c.upper() if c.isalnum() else ' ' for c in unicodedata.normalize('NFC', value or '')).split())


def reporting_lines(purchase):
    lines = purchase.get('lines', [])
    source = purchase['review'].get('reportingCsvDocumentId') or next((s['documentId'] for line in lines for s in line.get('sources', [])), '')
    return [line for line in lines if any(s['documentId'] == source for s in line.get('sources', []))]


def amount(raw, key):
    value = raw.get(key, '').strip().replace('$', '').replace(',', '')
    if value.startswith('(') and value.endswith(')'):
        value = '-' + value[1:-1]
    try:
        result = Decimal(value)
        return result if result.is_finite() else None
    except InvalidOperation:
        return None


def address_parts(value):
    text = normalize((value or '').split(',')[0].replace('#', ' UNIT '))
    match = re.match(r'^(\d+[A-Z]?) (.+)$', text)
    if not match or not any(c.isalpha() for c in match[2]):
        return None
    words = match[2].split()
    units = ('APT', 'APARTMENT', 'UNIT', 'STE', 'SUITE')
    unit_at = next((i for i, w in enumerate(words) if w in units), len(words))
    unit = ' '.join(words[unit_at + 1:])
    words = words[:unit_at]
    suffixes = {}
    for aliases in ('ST STREET', 'RD ROAD', 'DR DRIVE', 'AVE AVENUE', 'LN LANE', 'CT COURT', 'BLVD BOULEVARD', 'CIR CIRCLE', 'TRL TRAIL', 'PL PLACE', 'PKWY PARKWAY', 'PIKE PIKE', 'WAY WAY', 'TER TERRACE', 'HWY HIGHWAY'):
        short, long = aliases.split()
        suffixes[short] = suffixes[long] = long
    suffix_at = next((i for i in range(len(words) - 1, -1, -1) if words[i] in suffixes), None)
    suffix = suffixes[words[suffix_at]] if suffix_at is not None else ''
    if suffix_at is not None:
        words = words[:suffix_at]
    directions = dict(N='NORTH', S='SOUTH', E='EAST', W='WEST', NE='NORTHEAST', NW='NORTHWEST', SE='SOUTHEAST', SW='SOUTHWEST')
    return match[1], ' '.join(directions.get(w, w) for w in words), suffix, unit


def fresh_candidates(source, current, operation_names):
    text, street = normalize(source), address_parts(source)
    if not text:
        return []
    result = []
    for op, jobs in current.items():
        for job in jobs:
            customer = job.get('customer') or {}
            name = ' '.join(str(customer.get(k) or '') for k in ('first_name', 'last_name')).strip()
            address = job.get('address') or {}
            if not isinstance(address, dict):
                address = {}
            street_text = address.get('street') or address.get('street_line_1') or address.get('address_line_1') or ''
            full_address = ', '.join(str(x) for x in (street_text, address.get('street_line_2'), address.get('city'), address.get('state'), address.get('zip') or address.get('postal_code')) if x)
            target = address_parts(full_address)
            number = str(job.get('invoice_number') or job.get('job_number') or '').strip()
            if not number:
                continue
            rank, kind = 0, ''
            explicit = re.fullmatch(r'\s*(?:(?:JOB(?:\s+(?:NO\.?|NUMBER))?|PROJECT(?:\s+(?:NO\.?|NUMBER))?|PO)\s*[:#=\-]?\s*)?' + re.escape(number) + r'\s*', source, re.I)
            if explicit:
                rank, kind = 100, 'Job number'
            elif street and target and street[:2] == target[:2] and not (street[2] and target[2] and street[2] != target[2]) and not (street[3] and target[3] and street[3] != target[3]):
                rank, kind = (95, 'Street address') if street[2] and target[2] and bool(street[3]) == bool(target[3]) else (80, 'Partial address')
            elif not street:
                parts = normalize(name).split()
                if text == normalize(name):
                    rank, kind = 90, 'Homeowner name'
                elif parts and len(parts[-1]) >= 2 and parts[-1].isalpha() and parts[-1] in text.split():
                    rank, kind = 60, 'Last-name candidate'
                elif parts and len(parts[0]) >= 2 and parts[0].isalpha() and parts[0] in text.split():
                    rank, kind = 50, 'First-name candidate'
            if rank:
                result.append(dict(operationId=op, operation=operation_names[op], jobId=job['id'], jobNumber=number,
                    customer=name, customerId=customer.get('id') or '', address=full_address,
                    description=job.get('description') or job.get('name') or 'Description unavailable', rank=rank,
                    matchKind=kind, matchedReference=source, explanation='Fresh complete operation-scoped job lookup. Unique strongest matches may be assigned under owner authorization.'))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--unique-name-address', action='store_true', help='Owner-authorized unique strongest name/address matching, including unique partial names.')
    args = parser.parse_args()
    os.umask(0o077)
    settings = {}
    for line in Path('/opt/wrightapps/charlie-company/shared/charlie-company.env').read_text().splitlines():
        if line.strip() and not line.lstrip().startswith('#') and '=' in line:
            key, value = line.split('=', 1)
            settings[key.strip()] = shlex.split(value)[0] if value.startswith(('"', "'")) else value
    root = Path(settings.get('HomeDepotHistory__StoragePath', '/var/lib/charlie-company/home-depot-history'))
    connection = dict((k.strip().lower(), v.strip().strip('"')) for k, v in re.findall(r'(?:^|;)([^=;]+)=((?:"[^"]*")|[^;]*)', settings['ConnectionStrings__DefaultConnection']))
    query = '''SELECT json_build_object('operations', (SELECT json_agg(json_build_object('id', "Id", 'slug', "Slug")) FROM "LocalOperations" WHERE "IsActive"), 'jobs', (SELECT json_agg(json_build_object('operationId', "LocalOperationId", 'jobId', "ExternalId", 'jobNumber', "JobNumber")) FROM "HousecallProJobs"));'''
    result = subprocess.run(['psql', '-X', '-w', '-At', '-v', 'ON_ERROR_STOP=1', '-h', connection['host'], '-p', connection.get('port', '5432'), '-U', connection.get('username', connection.get('user id', '')), '-d', connection['database'], '-c', query],
                            env=dict(os.environ, PGPASSWORD=connection.get('password', ''), PGOPTIONS='-c default_transaction_read_only=on'), capture_output=True)
    if result.returncode:
        raise RuntimeError('Read-only synchronized job lookup failed; no assignments saved.')
    database = json.loads(result.stdout)
    local = database.get('jobs') or []
    current = {}
    for op in database['operations']:
        key = next((v for k, v in settings.items() if k.lower() == ('HousecallPro__OperationApiKeys__' + op['slug']).lower()), None)
        if not key and op['slug'].lower() == 'nashville':
            key = settings.get('HousecallPro__ApiKey')
        if not key or 'PLACEHOLDER' in key.upper():
            raise RuntimeError('Active operation has no usable HCP configuration; no assignments saved.')
        jobs, seen, expected = [], set(), None
        for page in range(1, 101):
            url = 'https://api.housecallpro.com/jobs?page_size=100&page=' + str(page)
            request = urllib.request.Request(url, headers={'Authorization': 'Bearer ' + key, 'Accept': 'application/json'})
            with urllib.request.urlopen(request, timeout=60) as response:
                payload = json.load(response)
            total = payload.get('total_pages')
            if not isinstance(total, int) or not 0 <= total <= 100 or expected is not None and expected != total or not isinstance(payload.get('jobs'), list):
                raise RuntimeError('Incomplete HCP pagination; no assignments saved.')
            expected = total
            for job in payload['jobs']:
                if not job.get('id') or job['id'] in seen:
                    raise RuntimeError('Inconsistent HCP identity; no assignments saved.')
                seen.add(job['id'])
                jobs.append(job)
            if page >= total:
                break
        else:
            raise RuntimeError('HCP page limit; no assignments saved.')
        current[op['id']] = jobs

    # FileShare.None on Linux uses flock. Participate in the application's writer
    # lock and publish the same immutable-revision + atomic-current protocol.
    with (root / 'writer.lock').open('a+b') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        state = json.loads((root / 'current.json').read_text())
        if state.get('schemaVersion') != 1 or state.get('accountScope') != settings.get('HomeDepotHistory__AccountScope'):
            raise RuntimeError('Storage scope mismatch; no assignments saved.')
        counts, jobs_used, transactions = Counter(), set(), set()
        operation_names = {o['id']: o['slug'] for o in database['operations']}
        refreshed = {}
        if args.unique_name_address:
            for purchase in state['purchases']:
                for line in reporting_lines(purchase):
                    raw = line['raw']
                    source = raw.get('Job Name') or raw.get('Project Name', '')
                    reference = normalize(source)
                    if reference not in refreshed:
                        refreshed[reference] = fresh_candidates(source, current, operation_names)
        now = datetime.now(timezone.utc).isoformat()
        actor = 'maintenance:owner-authorized-clear-job-matching'
        for purchase in state['purchases']:
            review = purchase['review']
            if review.get('status') == 'Excluded':
                continue
            lines = reporting_lines(purchase)
            existing = review.get('materialAllocations', [])
            before = json.dumps(existing, separators=(',', ':'))
            additions = []
            for line in lines:
                if any(a['lineId'] == line['id'] for a in existing):
                    counts['existing_line_preserved'] += 1
                    continue
                raw = line['raw']
                reference = normalize(raw.get('Job Name') or raw.get('Project Name', ''))
                if any(x['sourceReference'] == reference for x in state.get('jobReferenceLinks', [])):
                    counts['existing_reference_preserved'] += 1
                    continue
                if not reference or any(x['reference'] == reference for x in state.get('deletedCustomerReferences', [])):
                    counts['no_job_reference'] += 1
                    continue
                candidates = refreshed.get(reference, []) if args.unique_name_address else [c for c in review.get('candidates', []) if normalize(c.get('matchedReference')) == reference and c.get('rank') in (90, 100)]
                confirmed = [x for x in state.get('customerReviews', []) if x['reference'] == reference]
                if args.unique_name_address:
                    if confirmed:
                        candidates = [c for c in candidates if all(c['operationId'] == x['operationId'] and c['customerId'] == x['customerId'] for x in confirmed)]
                    best_rank = max((c['rank'] for c in candidates), default=0)
                    candidates = [c for c in candidates if c['rank'] == best_rank]
                unique = {(c['operationId'], c['jobId']): c for c in candidates}
                # Conflicting strong evidence never becomes an automatic choice.
                if len(unique) != 1:
                    counts['ambiguous_or_weak'] += 1
                    continue
                candidate = next(iter(unique.values()))
                op, job_id = candidate['operationId'], candidate['jobId']
                group_conflict = any(
                    a['operationId'] != op or a['jobId'] != job_id
                    for p in state['purchases'] if p['review'].get('status') != 'Excluded'
                    for l in reporting_lines(p) if normalize(l['raw'].get('Job Name') or l['raw'].get('Project Name', '')) == reference
                    for a in p['review'].get('materialAllocations', []) if a['lineId'] == l['id'])
                if group_conflict:
                    counts['existing_group_job_conflict'] += 1
                    continue
                found = [j for j in current.get(op, []) if j['id'] == job_id and str(j.get('invoice_number') or j.get('job_number') or '').strip() == candidate['jobNumber'].strip()]
                if len(found) != 1 or review.get('operationId') not in (None, op):
                    counts['identity_conflict'] += 1
                    continue
                job = found[0]
                customer = job.get('customer') or {}
                confirmed = [x for x in state.get('customerReviews', []) if x['reference'] == reference]
                if confirmed and any(x['operationId'] != op or x['customerId'] != customer.get('id') for x in confirmed):
                    counts['confirmed_customer_conflict'] += 1
                    continue
                if candidate['rank'] == 100:
                    # Require a bare number or explicit job/PO marker, never a
                    # house number or an arbitrary numeric token in a name.
                    number = re.escape(candidate['jobNumber'].strip())
                    source = raw.get('Job Name') or raw.get('Project Name', '')
                    if not re.fullmatch(r'\s*(?:(?:JOB(?:\s+(?:NO\.?|NUMBER))?|PROJECT(?:\s+(?:NO\.?|NUMBER))?|PO)\s*[:#=\-]?\s*)?' + number + r'\s*', source, re.I):
                        counts['job_number_requires_review'] += 1
                        continue
                elif not args.unique_name_address:
                    full_name = normalize(' '.join(str(customer.get(k) or '') for k in ('first_name', 'last_name')))
                    same_name = [(o, j['id']) for o, items in current.items() for j in items if normalize(' '.join(str((j.get('customer') or {}).get(k) or '') for k in ('first_name', 'last_name'))) == reference]
                    if reference != full_name or len(same_name) != 1:
                        counts['repeat_customer_or_name_requires_review'] += 1
                        continue
                synced = [j for j in local if j['operationId'] == op and normalize(j.get('jobNumber')) == normalize(candidate['jobNumber'])]
                if len(synced) != 1 or synced[0]['jobId'] != job_id:
                    counts['job_not_uniquely_available_to_report'] += 1
                    continue
                qty, cost = amount(raw, 'Quantity'), amount(raw, 'Extended Retail (before discount)')
                if qty is None or not qty or cost is None or qty != qty.quantize(Decimal('.0001')) or cost != cost.quantize(Decimal('.01')) or review.get('conflicts'):
                    counts['source_values_require_review'] += 1
                    continue
                additions.append(dict(lineId=line['id'], operationId=op, jobId=job_id, jobNumber=candidate['jobNumber'], quantity=float(qty), reportedAmount=float(cost)))
                counts['matched_lines'] += 1
                jobs_used.add((op, job_id))
            if additions:
                review['materialAllocations'] = existing + additions
                purchase['version'] += 1
                purchase['changedAt'] = now
                transactions.add(purchase['id'])
                state['audit'].append(dict(at=now, actor=actor, action='MaterialLinesAllocated', purchaseId=purchase['id'], beforeJson=before,
                    afterJson=json.dumps(review['materialAllocations'], separators=(',', ':')), reason='Owner authorized unique strongest name/address/job-number matching; complete live operation/job and synchronized report identity confirmed. Ambiguous jobs and source conflicts left for manual review. Existing assignments preserved; no accounting or catalog changes.'))
        if args.apply and transactions:
            state['revision'] += 1
            data = json.dumps(state, ensure_ascii=False, separators=(',', ':')).encode()
            revisions = root / 'revisions'
            revisions.mkdir(mode=0o700, exist_ok=True)
            revision = revisions / (str(state['revision']).zfill(16) + '-' + uuid.uuid4().hex + '.json')
            with revision.open('xb') as out:
                out.write(data)
                out.flush()
                os.fsync(out.fileno())
            temporary = root / (uuid.uuid4().hex + '.tmp')
            with temporary.open('xb') as out:
                out.write(data)
                out.flush()
                os.fsync(out.fileno())
            os.replace(temporary, root / 'current.json')
        print(json.dumps(dict(mode='applied' if args.apply else 'preview', transactions=len(transactions), jobs=len(jobs_used), counts=dict(counts))))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # API errors and subprocess diagnostics can contain private data. Keep
        # them off stdout/stderr; failure is closed before publishing a revision.
        raise SystemExit('Job matching stopped (' + type(error).__name__ + '); no successful completion is assumed.')
