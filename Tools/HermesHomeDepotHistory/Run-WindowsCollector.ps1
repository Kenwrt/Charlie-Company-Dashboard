param([string]$RuntimeRoot = $PSScriptRoot, [switch]$Verify, [switch]$SandboxDiagnostic, [switch]$ArchiveReplayOnly, [switch]$ReuseDedicatedProfile, [switch]$WaitForLogoff)
$ErrorActionPreference = 'Stop'
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'launcher-entry.json'), ('{"version":"20261104-2","verify":' + $Verify.ToString().ToLowerInvariant() + '}'))
$mutex = $null
$locked = $false
$stage = 'Lock'
$resultName = if ($Verify) { 'latest-verification.json' } else { 'latest-run.json' }
try {
    $mutex = [Threading.Mutex]::new($false, 'Local\CharlieCompany.HomeDepotAnalyst')
    $locked = $mutex.WaitOne(0)
    if (-not $locked) { exit 0 }
    $credential = $null
    if (-not $ReuseDedicatedProfile) {
        $stage = 'CredentialVaultRead'
        . (Join-Path $RuntimeRoot 'WindowsCredentialStore.ps1')
        $credential = Get-HomeDepotVaultCredential
    }
    # The app-owned vault entry is provisioned for this task's Windows identity.
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $RuntimeRoot 'venv\Scripts\python.exe'
    $start.Arguments = '"' + (Join-Path $RuntimeRoot 'windows_collector.py') + '"'
    if ($Verify) { $start.Arguments += ' --verify' }
    if ($ReuseDedicatedProfile) { $start.Arguments += ' --reuse-dedicated-profile' }
    if ($WaitForLogoff) {
        if (-not $Verify -or -not $ReuseDedicatedProfile) { throw 'Logoff verification requires dedicated profile verification.' }
        $start.Arguments += ' --wait-for-logoff'
    }
    if ($ArchiveReplayOnly) {
        if (-not $Verify) { throw 'Archive replay requires verification mode.' }
        $start.Arguments += ' --verify-archive-only'
    }
    if ($SandboxDiagnostic) {
        if (-not $Verify) { throw 'Sandbox diagnostic requires explicit verification mode.' }
        $start.Arguments += ' --sandbox-diagnostic'
    }
    $start.WorkingDirectory = $RuntimeRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $stage = 'WorkerLaunch'
    $process = [Diagnostics.Process]::Start($start)
    $stage = 'CredentialPipe'
    $payload = if ($ReuseDedicatedProfile) { '{"username":"","password":""}' } else { @{username=$credential.UserName; password=$credential.GetNetworkCredential().Password} | ConvertTo-Json -Compress }
    $process.StandardInput.WriteLine($payload)
    $process.StandardInput.Close()
    $payload = $null
    $credential = $null
    $stage = 'WorkerCompletion'
    $safeOutput = $process.StandardOutput.ReadToEndAsync()
    $discardErrors = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(900000)) { $process.Kill(); throw 'Collector exceeded its time limit.' }
    $output = $safeOutput.GetAwaiter().GetResult()
    $null = $discardErrors.GetAwaiter().GetResult()
    # Python emits bounded receipt metadata only, never browser diagnostics.
    $receipt = $output | ConvertFrom-Json
    $receipt | Add-Member -NotePropertyName completedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
    $receipt | Add-Member -NotePropertyName windowsSessionId -NotePropertyValue ([Diagnostics.Process]::GetCurrentProcess().SessionId) -Force
    if (-not $receipt.PSObject.Properties['desktopLogoffVerified']) {
        $receipt | Add-Member -NotePropertyName desktopLogoffVerified -NotePropertyValue $false
    }
    [IO.File]::WriteAllText((Join-Path $RuntimeRoot $resultName), ($receipt | ConvertTo-Json -Depth 8 -Compress))
    exit $process.ExitCode
} catch {
    $safeFailure = @{status='NeedsOwnerAction';reason='WindowsWorkerIncomplete';stage=$stage;exceptionType=$_.Exception.GetType().FullName;hresult=$_.Exception.HResult} | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $RuntimeRoot $resultName), $safeFailure)
    exit 1
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
}
