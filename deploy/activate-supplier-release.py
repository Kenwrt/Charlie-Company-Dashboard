"""API01 deployment: protected backup/configuration, atomic release and restart.

Run only for an explicitly authorized supplier-invoice release. Never emit secrets.
"""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shlex
import shutil
import subprocess
import sys

os.umask(0o077)
root = Path('/opt/wrightapps/charlie-company')
release = Path(sys.argv[1]).resolve()
if release.parent != root / 'releases' or not (release / 'CharleyCompany.Dashboard.Web.dll').is_file():
    raise SystemExit('Release path is not a staged dashboard release.')
current = root / 'current'
if not current.is_symlink():
    raise SystemExit('Current release must be a symlink; no deployment performed.')
previous = current.resolve()
stamp = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
backup = root / 'shared/backups' / ('supplier-invoices-' + stamp)
backup.mkdir(mode=0o700, parents=True)
env_path = root / 'shared/charlie-company.env'
original = env_path.read_text()
shutil.copy2(env_path, backup / 'charlie-company.env')
(backup / 'charlie-company.env').chmod(0o600)
shutil.copy2('/etc/systemd/system/charlie-company.service', backup / 'charlie-company.service')
values = {}
for line in original.splitlines():
    line = line.strip()
    if not line or line.startswith('#') or '=' not in line:
        continue
    name, value = line.split('=', 1)
    if value.startswith(('"', "'")):
        parts = shlex.split(value)
        value = parts[0] if parts else ''
    values[name] = value
connection = values.get('ConnectionStrings__DefaultConnection', '')
fields = {match.group(1).strip().lower(): match.group(2).strip().strip('"')
          for match in re.finditer(r'(?:^|;)([^=;]+)=((?:"[^"]*")|[^;]*)', connection)}
if not fields.get('database') or not fields.get('host'):
    raise SystemExit('Protected database configuration is incomplete; release was not switched.')
if len(sys.argv) > 2:
    # A matching server-side pg_dump may supply the archive without installing
    # system packages or copying database credentials between machines.
    supplied = Path(sys.argv[2]).resolve()
    if (root / 'shared/backups') not in supplied.parents:
        raise SystemExit('Preflight archive must be inside the private backup directory.')
    receipt = json.loads(supplied.with_suffix('.json').read_text())
    age = dt.datetime.now(dt.timezone.utc) - dt.datetime.fromisoformat(receipt['created_at'])
    archive = supplied.read_bytes()
    if receipt.get('database') != fields['database'] or not receipt.get('archive_valid') or not archive.startswith(b'PGDMP') \
            or age.total_seconds() < 0 or age.total_seconds() > 1800 \
            or hashlib.sha256(archive).hexdigest() != receipt.get('sha256'):
        raise SystemExit('Preflight backup receipt is invalid, stale or does not match this database; release was not switched.')
    shutil.copy2(supplied, backup / 'database.dump')
    shutil.copy2(supplied.with_suffix('.json'), backup / 'database.json')
else:
    with (backup / 'database.dump').open('xb') as output:
        result = subprocess.run(['pg_dump', '-w', '-Fc', '-h', fields['host'], '-p', fields.get('port', '5432'),
                                 '-U', fields.get('username', fields.get('user id', '')), '-d', fields['database']],
                                env=dict(os.environ, PGPASSWORD=fields.get('password', '')), stdout=output, stderr=subprocess.PIPE)
    if result.returncode:
        reason = 'PostgreSQL client/server version mismatch' if b'version mismatch' in result.stderr else 'pg_dump failed'
        raise SystemExit('Database backup failed (' + reason + '); release was not switched.')
key_path = root / 'shared/supplier-invoices-ingestion-key'
key = values.get('SupplierInvoices__IngestionKey') or secrets.token_urlsafe(48)
ap_key = values.get('SupplierInvoices__ApReadKey') or secrets.token_urlsafe(48)
if key == ap_key:
    raise SystemExit('Supplier ingestion and AP read credentials must be different.')
key_path.write_text(key)
key_path.chmod(0o600)
updates = {
    'SupplierInvoices__Enabled': 'true', 'SupplierInvoices__DecksDocksVendorId': '2',
    'SupplierInvoices__AutoPostVerifiedInvoices': 'false',
    'SupplierInvoices__DefaultPayingEntityCode': '',
    'SupplierInvoices__DefaultLocationSlug': 'nashville',
    'SupplierInvoices__AllowSingleLocationDefault': 'true',
    'SupplierInvoices__ParticipatingLocationSlugs__0': 'nashville',
    'SupplierInvoices__StoragePath': '/var/lib/charlie-company/supplier-invoices',
    'SupplierInvoices__IngestionKey': key, 'SupplierInvoices__ApReadKey': ap_key,
}
if '--enable-supplier-reconciliation' in sys.argv[3:]:
    updates['SupplierInvoices__ReconciliationEnabled'] = 'true'
# Remove prior participation entries so this activation serves Nashville alone.
lines = [line for line in original.splitlines() if line.split('=', 1)[0].strip() not in updates
         and not line.split('=', 1)[0].strip().startswith('SupplierInvoices__ParticipatingLocationSlugs__')]
lines.extend(name + '=' + value for name, value in updates.items())
temporary_env = env_path.with_name('charlie-company.env.supplier-new')
temporary_env.write_text('\n'.join(lines) + '\n')
temporary_env.chmod(0o600)
os.replace(temporary_env, env_path)
temporary_link = root / ('current-supplier-' + stamp)
temporary_link.symlink_to(release)
os.replace(temporary_link, current)
result = subprocess.run(['sudo', '-n', '/usr/bin/systemctl', 'restart', 'charlie-company.service'], capture_output=True)
if result.returncode:
    rollback = root / ('current-rollback-' + stamp)
    rollback.symlink_to(previous)
    os.replace(rollback, current)
    shutil.copy2(backup / 'charlie-company.env', env_path)
    raise SystemExit('Service restart failed; release pointer and protected configuration restored. No follow-up restart attempted.')
record = {'release': str(release), 'previous_release': str(previous), 'backup': str(backup),
          'service_restart': 'command-succeeded', 'ap_auto_post': False,
          'migration_verification': 'pending', 'hermes_activation': 'pending'}
(root / 'shared/supplier-invoices-release.json').write_text(json.dumps(record, indent=2))
print(json.dumps(record))
