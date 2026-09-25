Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

[int]$n = 23

$p1 = [UIntPtr]::new([uint64]$n)
if ($p1.ToUInt64() -ne 23) { throw "UIntPtr::new failed" }

$p2 = New-Object UIntPtr -ArgumentList ([uint64]$n)
if ($p2.ToUInt64() -ne 23) { throw "New-Object UIntPtr failed" }

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class UIntPtrProbe {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint flAllocationType,
        uint flProtect);
}
'@

# Binder-only reflection check: confirm parameter 3 is UIntPtr.
$m = [UIntPtrProbe].GetMethod("VirtualAllocEx")
$t = $m.GetParameters()[2].ParameterType
if ($t.FullName -ne "System.UIntPtr") { throw "PInvoke signature mismatch" }

Write-Host ("UINTPTR_NEW_OK=" + $p1.ToUInt64())
Write-Host ("UINTPTR_NEWOBJECT_OK=" + $p2.ToUInt64())
Write-Host "UINTPTR_SIGNATURE_OK"
