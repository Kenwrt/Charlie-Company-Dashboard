"""DB01: back up only the dashboard database with its matching server client."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess

os.umask(0o077)
root = Path('/home/ken/charlie-company-backups')
root.mkdir(mode=0o700, exist_ok=True)
root.chmod(0o700)
stamp = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
target = root / ('supplier-invoices-' + stamp + '.dump')
with target.open('xb') as output:
    result = subprocess.run(['docker', 'exec', 'postgres', 'sh', '-c',
                             'exec pg_dump -Fc -U "$POSTGRES_USER" -d CharlieCompany'], stdout=output, stderr=subprocess.PIPE)
if result.returncode:
    raise SystemExit('Server-side dashboard backup failed; no deployment authorized by this result.')
with target.open('rb') as archive:
    result = subprocess.run(['docker', 'exec', '-i', 'postgres', 'pg_restore', '--list'], stdin=archive,
                            stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
if result.returncode:
    raise SystemExit('Archive inspection failed; retain the file for private diagnosis.')
digest = hashlib.sha256(target.read_bytes()).hexdigest()
receipt = {'database': 'CharlieCompany', 'sha256': digest,
           'created_at': dt.datetime.now(dt.timezone.utc).isoformat(), 'archive_valid': True, 'client_major': 17}
target.with_suffix('.json').write_text(json.dumps(receipt))
print(json.dumps({'path': str(target), 'receipt': str(target.with_suffix('.json')), 'sha256': digest, 'archive_valid': True}))
