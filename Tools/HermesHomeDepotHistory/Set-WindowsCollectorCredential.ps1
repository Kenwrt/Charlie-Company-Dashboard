# Run directly in the owner's Windows desktop, not through an automation shell.
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$owner = [Security.Principal.WindowsIdentity]::GetCurrent()
if ($owner.Name -ine 'WrightServer\serveradmin') { throw 'Run as WrightServer\serveradmin.' }
. (Join-Path $PSScriptRoot 'WindowsCredentialStore.ps1')
if ($CheckOnly) {
    $status = @{stage='NativeCredentialCheck';storage='WindowsCredentialManager';utc=[DateTime]::UtcNow.ToString('o');runtimeRoot=$PSScriptRoot;credentialExists=(Get-HomeDepotVaultCredential -ExistsOnly);windowsSessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId}
    $status | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'native-credential-status.json')
    $status | ConvertTo-Json -Compress | Write-Host
    exit 0
}
$secret = Read-Host 'Home Depot password (masked; stored only with Windows CurrentUser encryption)' -AsSecureString
if ($secret.Length -eq 0) { throw 'No credential was saved.' }
$credential = [Management.Automation.PSCredential]::new('ken@charliecompany.pro', $secret)
Set-HomeDepotVaultCredential -Credential $credential
$credential = $null
$secret = $null
@{stage='NativeCredentialSaved';storage='WindowsCredentialManager';utc=[DateTime]::UtcNow.ToString('o');runtimeRoot=$PSScriptRoot;credentialExists=(Get-HomeDepotVaultCredential -ExistsOnly);windowsSessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId} | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'native-credential-status.json')
Write-Host 'Credential saved in the native task runtime. The registered Windows task can now be verified.'
