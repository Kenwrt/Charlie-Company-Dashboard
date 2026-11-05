param([Parameter(Mandatory)][string]$SourcePath)
$ErrorActionPreference = 'Stop'
$source = Get-Item -LiteralPath $SourcePath
if ($source.Extension -ine '.csv' -or $source.Length -eq 0 -or $source.Length -gt 20MB) { throw 'Bounded CSV required.' }
$sha = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
# Hash-named file is authoritative; scp to a unique staging name, never overwrite an original.
$temporaryName = 'verify-' + [Guid]::NewGuid().ToString('N') + '.part'
& scp -q -o BatchMode=yes -o ConnectTimeout=15 $SourcePath ('ken@10.168.168.161:/srv/ai/hermes/home-depot-history/downloads/' + $temporaryName)
if ($LASTEXITCODE -ne 0) { throw 'Verification source transfer failed.' }
$script = @'
import hashlib,json,os,subprocess,sys
from pathlib import Path
os.umask(0o077)
root=Path('/srv/ai/hermes/home-depot-history')
sha,name=sys.argv[1:]
temporary=root/'downloads'/name
data=temporary.read_bytes()
if hashlib.sha256(data).hexdigest()!=sha or b'Date,Store Number,' not in data[:65536]:
 raise SystemExit('Source integrity failure')
target=root/'downloads'/(sha+'.csv')
if target.exists():
 if hashlib.sha256(target.read_bytes()).hexdigest()!=sha: raise SystemExit('Archive integrity failure')
 temporary.unlink()
else: temporary.rename(target)
target.chmod(0o600)
def archive():
 result=subprocess.run([str(root/'venv/bin/python'),str(root/'archive_downloads.py')],capture_output=True,text=True)
 if result.returncode: raise SystemExit('Archive operation incomplete')
 return json.loads(result.stdout)
first=archive()
replay=archive()
if replay['new_originals']!=0 or first['total_originals']!=replay['total_originals']:
 raise SystemExit('Replay invariant failed')
if hashlib.sha256((root/'inbox'/(sha+'.csv')).read_bytes()).hexdigest()!=sha:
 raise SystemExit('Archived original integrity failure')
print(json.dumps({'status':'ArchiveReplayVerified','sha256':sha,'firstNewOriginals':first['new_originals'],'totalOriginals':replay['total_originals'],'replayNewOriginals':replay['new_originals'],'dashboardHandoff':'Deferred'}))
'@
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))
& ssh -o BatchMode=yes -o ConnectTimeout=15 ken@10.168.168.161 "python3 -c 'import base64;exec(base64.b64decode(""$encoded""))' '$sha' '$temporaryName'"
if ($LASTEXITCODE -ne 0) { throw 'Archive replay verification incomplete.' }
