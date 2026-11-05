# Run interactively as the Windows identity that will own the unattended task.
# Passwords never appear in arguments, source, environment variables or output.
param([string]$RuntimeRoot = 'C:\Users\serveradmin\AppData\Local\CharlieCompany\HomeDepotAnalyst', [switch]$Resume, [switch]$UseConfirmedSharedPassword, [switch]$SandboxDiagnostic, [switch]$ArchiveReplayOnly, [switch]$ReuseDedicatedProfile, [switch]$WaitForLogoff)
$ErrorActionPreference = 'Stop'
if ((Get-TimeZone).Id -ne 'Central Standard Time') { throw 'Expected WrightServer Central time zone.' }
$owner = [Security.Principal.WindowsIdentity]::GetCurrent()
$ownerSid = $owner.User.Value
# This installation belongs to serveradmin. Elevation must not silently redirect
# DPAPI credentials into another administrator's profile or change their ACL.
$expectedOwner = ([Security.Principal.NTAccount]::new('WrightServer','serveradmin')).Translate([Security.Principal.SecurityIdentifier]).Value
if ($ownerSid -ne $expectedOwner) {
    throw 'Use a PowerShell window running as WrightServer\serveradmin. The saved Home Depot credential belongs to that Windows identity; do not re-enter passwords under another account.'
}
$RuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot)
New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null
& icacls.exe $RuntimeRoot /inheritance:r /grant:r "*${ownerSid}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Private runtime ACL setup failed.' }
foreach ($name in @('windows_collector.py','Run-WindowsCollector.ps1','Publish-OwnerChromeExport.ps1','Verify-ArchiveReplay.ps1','Set-WindowsCollectorCredential.ps1','WindowsCredentialStore.ps1','windows_sessions.py')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $RuntimeRoot $name) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-WindowsCollector.ps1') -Destination (Join-Path $RuntimeRoot 'Run-WindowsCollector-v2.ps1') -Force
if (-not (Test-Path -LiteralPath (Join-Path $RuntimeRoot 'venv\Scripts\python.exe'))) {
    & python -m venv (Join-Path $RuntimeRoot 'venv')
    if ($LASTEXITCODE -ne 0) { throw 'Private Python environment setup failed.' }
}
if (-not $Resume) {
    & (Join-Path $RuntimeRoot 'venv\Scripts\python.exe') -m pip install 'playwright>=1.50,<2'
    if ($LASTEXITCODE -ne 0) { throw 'Browser dependency installation failed.' }
    Write-Host 'Enter the HOME DEPOT password only in this local masked prompt.'
    $homeDepotCredential = [Management.Automation.PSCredential]::new('ken@charliecompany.pro', (Read-Host 'HOME DEPOT password (masked)' -AsSecureString))
    if (-not $homeDepotCredential -or $homeDepotCredential.Password.Length -eq 0) { throw 'Home Depot credential entry cancelled.' }
    $homeDepotCredential | Export-Clixml -LiteralPath (Join-Path $RuntimeRoot 'credential.xml')
    $homeDepotCredential = $null
} elseif (-not (Test-Path -LiteralPath (Join-Path $RuntimeRoot 'credential.xml'))) {
    throw 'Encrypted Home Depot credential is missing; run setup without Resume.'
}
[IO.File]::WriteAllText((Join-Path $RuntimeRoot 'setup-status.json'), '{"stage":"WindowsCredentialRequired","tasksRegistered":false}')
if ($UseConfirmedSharedPassword) {
    # Only for the owner's explicit confirmation that the protected password also
    # authenticates this Windows identity. Do not persist a second credential.
    $storedCredential = Import-Clixml -LiteralPath (Join-Path $RuntimeRoot 'credential.xml')
    $windowsCredential = [Management.Automation.PSCredential]::new($owner.Name, $storedCredential.Password)
    $storedCredential = $null
} else {
    Write-Host 'Next enter the WINDOWS account password, not the Home Depot password. Task Scheduler needs a password logon to run while signed out.'
    $windowsCredential = [Management.Automation.PSCredential]::new($owner.Name, (Read-Host 'WINDOWS account password for Task Scheduler (masked)' -AsSecureString))
}
if (-not $windowsCredential -or $windowsCredential.UserName -ne $owner.Name) { throw 'Use the current Windows identity for DPAPI access.' }
$taskHost = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'
if (-not (Test-Path -LiteralPath $taskHost)) { throw 'The installed PowerShell 7 task host is missing.' }
function New-CollectorTaskAction([switch]$Verification) {
    # Inline bootstrap respects script execution policy and records failures before
    # the worker starts. This contains paths and code only, never credentials.
    $runnerLiteral = (Join-Path $RuntimeRoot 'Run-WindowsCollector-v2.ps1').Replace("'", "''")
    $statusLiteral = (Join-Path $RuntimeRoot $(if ($Verification) {'verification-bootstrap.json'} else {'daily-bootstrap.json'})).Replace("'", "''")
    $runtimeLiteral = $RuntimeRoot.Replace("'", "''")
    $hostLiteral = $taskHost.Replace("'", "''")
    # Deploy a pinned, compressed source snapshot inside the actual batch session.
    # It carries only worker code; encrypted credentials and browser data are excluded.
    $verifyArgument = if ($Verification) { ' -Verify' } else { '' }
    if ($Verification -and $SandboxDiagnostic) { $verifyArgument += ' -SandboxDiagnostic' }
    if ($Verification -and $ArchiveReplayOnly) { $verifyArgument += ' -ArchiveReplayOnly' }
    if ($Verification -and $ReuseDedicatedProfile) { $verifyArgument += ' -ReuseDedicatedProfile' }
    if ($Verification -and $WaitForLogoff) { $verifyArgument += ' -WaitForLogoff' }
    $bootstrap = @"
`$ErrorActionPreference = 'Stop'
try {
    `$previousVerificationPath = Join-Path '$runtimeLiteral' 'verification-bootstrap.json'
    if (Test-Path -LiteralPath `$previousVerificationPath) {
        `$priorReceipt = Get-Content -LiteralPath `$previousVerificationPath -Raw | ConvertFrom-Json
        if (`$priorReceipt.receipt.authHttpStatus -eq 403) {
            [IO.File]::WriteAllText((Join-Path '$runtimeLiteral' 'auth-access-block.json'), '{"reason":"SupplierAccessDenied","httpStatus":403}')
        }
    }
    [IO.File]::WriteAllText('$statusLiteral', (@{stage='TaskHostStarted';utc=[DateTime]::UtcNow.ToString('o');sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId;runnerExists=(Test-Path -LiteralPath '$runnerLiteral')} | ConvertTo-Json -Compress))
    . (Join-Path '$runtimeLiteral' 'WindowsCredentialStore.ps1')
    if (-not (Get-HomeDepotVaultCredential -ExistsOnly)) {
        [IO.File]::WriteAllText('$statusLiteral', (@{stage='AwaitingWindowsVaultCredential';sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId;utc=[DateTime]::UtcNow.ToString('o');taskIdentity=[Security.Principal.WindowsIdentity]::GetCurrent().Name} | ConvertTo-Json -Compress))
        exit 2
    }
    `$python = Join-Path '$runtimeLiteral' 'venv\Scripts\python.exe'
    & `$python -c 'import playwright.sync_api' *> `$null
    if (`$LASTEXITCODE -ne 0) {
        [IO.File]::WriteAllText('$statusLiteral', '{"stage":"InstallingPrivateBrowserDependency","sessionId":0}')
        & `$python -m pip install 'playwright>=1.50,<2' *> `$null
        if (`$LASTEXITCODE -ne 0) { throw 'Private browser dependency setup failed' }
    }
    & '$hostLiteral' -NoProfile -NonInteractive -File '$runnerLiteral'$verifyArgument
    `$workerExit = `$LASTEXITCODE
    `$resultFile = Join-Path '$runtimeLiteral' '$(if ($Verification) {'latest-verification.json'} else {'latest-run.json'})'
    `$receipt = if (Test-Path -LiteralPath `$resultFile) { Get-Content -LiteralPath `$resultFile -Raw | ConvertFrom-Json } else { `$null }
    [IO.File]::WriteAllText('$statusLiteral', (@{stage='TaskWorkerFinished';utc=[DateTime]::UtcNow.ToString('o');sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId;workerExit=`$workerExit;receipt=`$receipt} | ConvertTo-Json -Depth 10 -Compress))
    exit `$workerExit
} catch {
    [IO.File]::WriteAllText('$statusLiteral', (@{stage='TaskHostFailed';exceptionType=`$_.Exception.GetType().FullName;hresult=`$_.Exception.HResult;runnerExists=(Test-Path -LiteralPath '$runnerLiteral');errorId=`$_.FullyQualifiedErrorId} | ConvertTo-Json -Compress))
    exit 1
}
"@
    $outerBytes = [Text.Encoding]::UTF8.GetBytes($bootstrap)
    $outerStream = [IO.MemoryStream]::new()
    $outerGzip = [IO.Compression.GZipStream]::new($outerStream, [IO.Compression.CompressionLevel]::Optimal, $true)
    $outerGzip.Write($outerBytes, 0, $outerBytes.Length)
    $outerGzip.Dispose()
    $outer64 = [Convert]::ToBase64String($outerStream.ToArray())
    $outerStream.Dispose()
    $entryCommand = "`$s=[IO.MemoryStream]::new([Convert]::FromBase64String('$outer64'));`$g=[IO.Compression.GZipStream]::new(`$s,[IO.Compression.CompressionMode]::Decompress);`$r=[IO.StreamReader]::new(`$g);& ([scriptblock]::Create(`$r.ReadToEnd()))"
    $bootstrap64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($entryCommand))
    if ($bootstrap64.Length -gt 32000) { throw 'Worker bootstrap exceeds Windows command-line limit.' }
    New-ScheduledTaskAction -Execute $taskHost -Argument ('-NoProfile -NonInteractive -EncodedCommand ' + $bootstrap64)
}
# Deploy each public code artifact separately in the native batch context. Passwords
# remain exclusively in the credential APIs; deployment command text contains code only.
$deploymentPrincipal = New-ScheduledTaskPrincipal -UserId $owner.Name -LogonType Password -RunLevel Limited
foreach ($artifact in @('windows_collector.py','Run-WindowsCollector.ps1','Publish-OwnerChromeExport.ps1','Verify-ArchiveReplay.ps1','Set-WindowsCollectorCredential.ps1','WindowsCredentialStore.ps1','windows_sessions.py')) {
    $targetName = if ($artifact -eq 'Run-WindowsCollector.ps1') { 'Run-WindowsCollector-v2.ps1' } else { $artifact }
    $targetLiteral = (Join-Path $RuntimeRoot $targetName).Replace("'", "''")
    $receiptLiteral = (Join-Path $RuntimeRoot 'deployment-progress.json').Replace("'", "''")
    $sourceBytes = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $artifact))
    $stream = [IO.MemoryStream]::new()
    $gzip = [IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionLevel]::Optimal,$true)
    $gzip.Write($sourceBytes,0,$sourceBytes.Length); $gzip.Dispose()
    $artifact64 = [Convert]::ToBase64String($stream.ToArray()); $stream.Dispose()
    $nonce = [Guid]::NewGuid().ToString('N')
    $deployCode = @"
`$ErrorActionPreference='Stop'
`$s=[IO.MemoryStream]::new([Convert]::FromBase64String('$artifact64'))
`$g=[IO.Compression.GZipStream]::new(`$s,[IO.Compression.CompressionMode]::Decompress)
`$out=[IO.MemoryStream]::new(); `$g.CopyTo(`$out); `$g.Dispose()
if ((Test-Path -LiteralPath '$targetLiteral') -and ((Get-Item -LiteralPath '$targetLiteral').Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected runtime link' }
[IO.File]::WriteAllBytes('$targetLiteral',`$out.ToArray()); `$out.Dispose()
[IO.File]::WriteAllText('$receiptLiteral','{"nonce":"$nonce","artifact":"$targetName"}')
"@
    $deploy64=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($deployCode))
    if ($deploy64.Length -gt 32000) { throw 'Deployment artifact exceeds command-line limit.' }
    $deployAction=New-ScheduledTaskAction -Execute $taskHost -Argument ('-NoProfile -NonInteractive -EncodedCommand '+$deploy64)
    $deployTask=New-ScheduledTask -Action $deployAction -Principal $deploymentPrincipal -Description 'On-demand private code deployment for Home Depot Analyst; no recurring trigger or credential payload.'
    Register-ScheduledTask -TaskName 'CharlieCompany-HomeDepotAnalyst-Deploy' -InputObject $deployTask -User $owner.Name -Password $windowsCredential.GetNetworkCredential().Password -Force | Out-Null
    Start-ScheduledTask -TaskName 'CharlieCompany-HomeDepotAnalyst-Deploy'
    $deployed=$false
    for($poll=0;$poll -lt 30;$poll++) {
        Start-Sleep -Milliseconds 500
        $progressPath=Join-Path $RuntimeRoot 'deployment-progress.json'
        if(Test-Path -LiteralPath $progressPath) {
            $progress=Get-Content -LiteralPath $progressPath -Raw | ConvertFrom-Json
            if($progress.nonce -eq $nonce) { $deployed=$true; break }
        }
    }
    if(-not $deployed) { throw 'Native artifact deployment did not acknowledge completion.' }
}
$action = New-CollectorTaskAction
$trigger = New-ScheduledTaskTrigger -Daily -At '07:00'
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 20) -WakeToRun
$definition = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal (New-ScheduledTaskPrincipal -UserId $owner.Name -LogonType Password -RunLevel Limited) -Description 'Hermes Home Depot Analyst: private headless collection, daily 07:00 Central. No personal browser dependency.'
try {
    Register-ScheduledTask -TaskName 'CharlieCompany-HomeDepotAnalyst' -InputObject $definition -User $owner.Name -Password $windowsCredential.GetNetworkCredential().Password -Force | Out-Null
    $verificationAction = New-CollectorTaskAction -Verification
    $verificationDefinition = New-ScheduledTask -Action $verificationAction -Settings $settings -Principal (New-ScheduledTaskPrincipal -UserId $owner.Name -LogonType Password -RunLevel Limited) -Description 'On-demand Home Depot fresh-login and archive replay verification. No recurring trigger.'
    Register-ScheduledTask -TaskName 'CharlieCompany-HomeDepotAnalyst-Verification' -InputObject $verificationDefinition -User $owner.Name -Password $windowsCredential.GetNetworkCredential().Password -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $RuntimeRoot 'setup-status.json'), '{"stage":"TasksRegistered","tasksRegistered":true}')
} catch {
    $failure = @{stage='TaskRegistrationFailed';tasksRegistered=$false;hresult=$_.Exception.HResult} | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $RuntimeRoot 'setup-status.json'), $failure)
    throw 'Task registration failed; bounded error recorded in setup-status.json. No success is claimed.'
} finally { $windowsCredential = $null }
Write-Host 'Windows task registered. Cutover and fresh-session/login/download/replay verification remain pending; do not claim production readiness.'
