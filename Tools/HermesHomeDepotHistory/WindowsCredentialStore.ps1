# Access only this application's explicitly provisioned generic credential.
if (-not ('HomeDepotVault.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace HomeDepotVault {
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
 public struct Credential {
  public uint Flags,Type; public string TargetName,Comment;
  public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
  public uint CredentialBlobSize; public IntPtr CredentialBlob;
  public uint Persist,AttributeCount; public IntPtr Attributes;
  public string TargetAlias,UserName;
 }
 public static class Native {
  [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]
  [return:MarshalAs(UnmanagedType.Bool)] public static extern bool Write(ref Credential c,uint flags);
  [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]
  [return:MarshalAs(UnmanagedType.Bool)] public static extern bool Read(string target,uint type,uint flags,out IntPtr c);
  [DllImport("advapi32.dll",EntryPoint="CredFree")] public static extern void Free(IntPtr c);
 }
}
'@
}
function Set-HomeDepotVaultCredential([Management.Automation.PSCredential]$Credential) {
    $buffer = [Runtime.InteropServices.Marshal]::SecureStringToCoTaskMemUnicode($Credential.Password)
    try {
        $record = [HomeDepotVault.Credential]::new()
        $record.Type=1; $record.TargetName='CharlieCompany.HomeDepotAnalyst'; $record.UserName=$Credential.UserName
        $record.Persist=2 # Local-machine persistence for this user across logon sessions.
        $record.CredentialBlobSize=$Credential.Password.Length*2; $record.CredentialBlob=$buffer
        if (-not [HomeDepotVault.Native]::Write([ref]$record,0)) { throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
    } finally { [Runtime.InteropServices.Marshal]::ZeroFreeCoTaskMemUnicode($buffer) }
}
function Get-HomeDepotVaultCredential([switch]$ExistsOnly) {
    $pointer=[IntPtr]::Zero
    if (-not [HomeDepotVault.Native]::Read('CharlieCompany.HomeDepotAnalyst',1,0,[ref]$pointer)) {
        $code=[Runtime.InteropServices.Marshal]::GetLastWin32Error()
        if ($ExistsOnly -and $code -eq 1168) { return $false }
        throw [ComponentModel.Win32Exception]::new($code)
    }
    try {
        if ($ExistsOnly) { return $true }
        $record=[Runtime.InteropServices.Marshal]::PtrToStructure($pointer,[type][HomeDepotVault.Credential])
        if ($record.CredentialBlobSize -gt 4096 -or $record.CredentialBlobSize % 2) { throw 'Invalid application credential.' }
        $secure=[Security.SecureString]::new()
        for($offset=0;$offset -lt $record.CredentialBlobSize;$offset+=2) {
            $secure.AppendChar([char]([Runtime.InteropServices.Marshal]::ReadInt16($record.CredentialBlob,$offset) -band 0xffff))
        }
        $secure.MakeReadOnly()
        return [Management.Automation.PSCredential]::new($record.UserName,$secure)
    } finally { [HomeDepotVault.Native]::Free($pointer) }
}
