param(
    [Parameter(Mandatory)][string]$SourcePath,
    [Parameter(Mandatory)][guid]$RunId,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9 /.,:()_-]{1,160}$')][string]$RangeLabel
)
$ErrorActionPreference = 'Stop'
$source = Get-Item -LiteralPath $SourcePath
if ($source.Extension -ine '.csv' -or $source.Length -gt 20MB -or $source.Length -eq 0) { throw 'Supply a bounded detailed-history CSV.' }
# Never print purchase rows, recipient/account values, credentials or browser state.
$sha = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$temporaryName = 'owner-chrome-' + $RunId.ToString('N') + '.part'
$destination = 'ken@10.168.168.161:/srv/ai/hermes/home-depot-history/downloads/' + $temporaryName
& scp -q -o BatchMode=yes -o ConnectTimeout=15 $source.FullName $destination
if ($LASTEXITCODE -ne 0) { throw 'Private original transfer failed; the request remains incomplete.' }
$payload = @{runId=$RunId.ToString(); sha256=$sha; rangeLabel=$RangeLabel; temporaryName=$temporaryName} | ConvertTo-Json -Compress
$script = @'
import base64,hashlib,json,os,subprocess,sys
from pathlib import Path
os.umask(0o077)
payload=json.loads(base64.b64decode(sys.argv[1]))
root=Path('/srv/ai/hermes/home-depot-history')
temporary=root/'downloads'/payload['temporaryName']
data=temporary.read_bytes()
if len(data)>20*1024*1024 or b'Date,Store Number,' not in data[:65536] or hashlib.sha256(data).hexdigest()!=payload['sha256']:
 raise SystemExit('Original type or integrity mismatch; no receipt committed')
target=root/'downloads'/(payload['sha256']+'.csv')
if target.exists():
 if hashlib.sha256(target.read_bytes()).hexdigest()!=payload['sha256']: raise SystemExit('Existing original integrity failure')
 temporary.unlink()
else: temporary.rename(target)
target.chmod(0o600)
result=subprocess.run([str(root/'venv/bin/python'),str(root/'archive_downloads.py')],capture_output=True,text=True)
if result.returncode: raise SystemExit('Private archiving failed; no receipt committed')
result=subprocess.run([str(root/'venv/bin/python'),str(root/'owner_chrome_dispatch.py'),'complete','--run-id',payload['runId'],'--sha256',payload['sha256'],'--range-label',payload['rangeLabel']],capture_output=True,text=True)
if result.returncode: raise SystemExit('Original archived but daily receipt incomplete; replay is safe')
print(result.stdout.strip())
'@
$script64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))
$payload64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
$remote = "python3 -c 'import base64;exec(base64.b64decode(""$script64""))' '$payload64'"
& ssh -o BatchMode=yes -o ConnectTimeout=15 ken@10.168.168.161 $remote
if ($LASTEXITCODE -ne 0) { throw 'Archive handoff did not commit its daily receipt; no completion is claimed.' }
