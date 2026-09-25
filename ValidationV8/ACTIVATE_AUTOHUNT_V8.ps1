param(
    [switch]$SelfTest,
    [switch]$ProbeOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Info([string]$s) { Write-Host ("[INFO] " + $s) }
function Ok([string]$s)   { Write-Host ("[OK]   " + $s) -ForegroundColor Green }
function Warn([string]$s) { Write-Host ("[WARN] " + $s) -ForegroundColor Yellow }

$NativeCode = @'
using System;
using System.Runtime.InteropServices;

public static class Win32AutoHuntV8 {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        IntPtr nSize,
        out IntPtr lpNumberOfBytesRead
    );

    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        IntPtr nSize,
        out IntPtr lpNumberOfBytesWritten
    );

    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint flAllocationType,
        uint flProtect
    );

    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualFreeEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint dwFreeType
    );

    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr CreateRemoteThread(
        IntPtr hProcess,
        IntPtr lpThreadAttributes,
        UIntPtr dwStackSize,
        IntPtr lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId
    );

    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);
}
'@

Add-Type -TypeDefinition $NativeCode -Language CSharp

$PROCESS_CREATE_THREAD = 0x0002
$PROCESS_VM_OPERATION = 0x0008
$PROCESS_VM_READ = 0x0010
$PROCESS_VM_WRITE = 0x0020
$PROCESS_QUERY_INFORMATION = 0x0400
$ACCESS = $PROCESS_CREATE_THREAD -bor $PROCESS_VM_OPERATION -bor $PROCESS_VM_READ -bor $PROCESS_VM_WRITE -bor $PROCESS_QUERY_INFORMATION

$MEM_COMMIT = 0x1000
$MEM_RESERVE = 0x2000
$MEM_RELEASE = 0x8000
$PAGE_EXECUTE_READWRITE = 0x40
$WAIT_OBJECT_0 = 0x00000000

$RuntimeImageBase = [uint32]0x00400000
$PledgeObjectGlobalRva = [uint32](0x015E74E0 - 0x00400000)
$PledgeVtableRva = [uint32](0x0122A8BC - 0x00400000)
$ActiveByteOffset = [uint32]0x84

function Read-RemoteBytes(
    [IntPtr]$Handle,
    [UInt64]$Address,
    [int]$Count
) {
    [byte[]]$buf = New-Object byte[] $Count
    [IntPtr]$read = [IntPtr]::Zero

    $ok = [Win32AutoHuntV8]::ReadProcessMemory(
        $Handle,
        [IntPtr]([Int64]$Address),
        $buf,
        [IntPtr]$Count,
        [ref]$read
    )

    if (-not $ok -or $read.ToInt64() -ne $Count) {
        return $null
    }

    return $buf
}

function Read-RemoteU32(
    [IntPtr]$Handle,
    [UInt64]$Address
) {
    $b = Read-RemoteBytes $Handle $Address 4
    if ($null -eq $b) { return $null }
    return [BitConverter]::ToUInt32($b, 0)
}

function Get-MjlinCandidates {
    $list = New-Object System.Collections.ArrayList

    foreach ($p in @(Get-Process -ErrorAction SilentlyContinue)) {
        if ($p.ProcessName -notlike "mjlin*") { continue }

        try {
            $base = [UInt64]$p.MainModule.BaseAddress.ToInt64()
        }
        catch {
            continue
        }

        $h = [Win32AutoHuntV8]::OpenProcess($ACCESS, $false, $p.Id)
        if ($h -eq [IntPtr]::Zero) {
            continue
        }

        try {
            $globalAddr = $base + [UInt64]$PledgeObjectGlobalRva
            $objectPtr = Read-RemoteU32 $h $globalAddr
            if ($null -eq $objectPtr -or $objectPtr -eq 0) {
                continue
            }

            $vtable = Read-RemoteU32 $h ([UInt64]$objectPtr)
            if ($null -eq $vtable) {
                continue
            }

            $expectedVtable = [uint32]($base + [UInt64]$PledgeVtableRva)
            if ($vtable -ne $expectedVtable) {
                continue
            }

            $activeBytes = Read-RemoteBytes $h ([UInt64]$objectPtr + [UInt64]$ActiveByteOffset) 1
            if ($null -eq $activeBytes) {
                continue
            }

            [void]$list.Add([ordered]@{
                ProcessId = $p.Id
                Base = $base
                GlobalAddress = $globalAddr
                Object = [uint32]$objectPtr
                Vtable = [uint32]$vtable
                Active = [int]$activeBytes[0]
            })
        }
        finally {
            [void][Win32AutoHuntV8]::CloseHandle($h)
        }
    }

    return ,$list
}

function Build-ActivationStub([uint32]$GlobalAddress) {
    # x86:
    # mov eax,[absolute_global]
    # test eax,eax
    # je done
    # mov ecx,eax
    # mov edx,[eax]
    # push 1
    # call dword ptr [edx+44h]
    # done: xor eax,eax
    # ret 4
    [byte[]]$stub = @(
        0xA1,0x00,0x00,0x00,0x00,
        0x85,0xC0,
        0x74,0x09,
        0x8B,0xC8,
        0x8B,0x10,
        0x6A,0x01,
        0xFF,0x52,0x44,
        0x33,0xC0,
        0xC2,0x04,0x00
    )

    [byte[]]$addr = [BitConverter]::GetBytes($GlobalAddress)
    [Array]::Copy($addr, 0, $stub, 1, 4)
    return $stub
}

function Invoke-SelfTest {
    [byte[]]$stub = Build-ActivationStub ([uint32]0x12345678)

    if ($stub.Length -ne 23) {
        throw ("Unexpected stub length: " + $stub.Length)
    }

    if ($stub[0] -ne 0xA1) {
        throw "Self-test failed: MOV opcode."
    }

    $embedded = [BitConverter]::ToUInt32($stub, 1)
    if ($embedded -ne [uint32]0x12345678) {
        throw "Self-test failed: embedded global address."
    }

    if ($stub[7] -ne 0x74 -or $stub[8] -ne 0x09) {
        throw "Self-test failed: JE displacement."
    }

    if ($stub[15] -ne 0xFF -or $stub[16] -ne 0x52 -or $stub[17] -ne 0x44) {
        throw "Self-test failed: virtual Activate call."
    }

    if ($stub[20] -ne 0xC2 -or $stub[21] -ne 0x04 -or $stub[22] -ne 0x00) {
        throw "Self-test failed: thread return."
    }

    Ok "Runtime self-test passed."
}

if ($SelfTest) {
    Invoke-SelfTest
    Write-Host "SELF TEST COMPLETE - NO GAME PROCESS WAS TOUCHED." -ForegroundColor Green
    exit 0
}

$candidates = @(Get-MjlinCandidates)

if ($candidates.Count -eq 0) {
    throw "No validated mjlin process with a loaded PledgeNoticeUI object was found. Enter the world first, then run this tool."
}

if ($candidates.Count -ne 1) {
    Write-Host "Validated candidates:" -ForegroundColor Yellow
    foreach ($c in $candidates) {
        Write-Host ("PID=" + $c.ProcessId + " Base=0x" + ([UInt64]$c.Base).ToString("X8") + " Object=0x" + ([uint32]$c.Object).ToString("X8"))
    }
    throw ("Expected exactly one validated game process, found " + $candidates.Count + ".")
}

$candidate = $candidates[0]

Write-Host ""
Info ("PID: " + $candidate.ProcessId)
Info ("mjlin base: 0x" + ([UInt64]$candidate.Base).ToString("X8"))
Info ("PledgeNoticeUI object: 0x" + ([uint32]$candidate.Object).ToString("X8"))
Info ("vtable verified: 0x" + ([uint32]$candidate.Vtable).ToString("X8"))
Info ("active before: " + $candidate.Active)

if ($ProbeOnly) {
    Ok "Probe passed. Object pointer and vtable match the captured client build."
    Write-Host "PROBE ONLY COMPLETE - NO GAME MEMORY WAS WRITTEN." -ForegroundColor Green
    exit 0
}

$hProcess = [Win32AutoHuntV8]::OpenProcess($ACCESS, $false, [int]$candidate.ProcessId)
if ($hProcess -eq [IntPtr]::Zero) {
    throw ("OpenProcess failed. Win32=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
}

$remote = [IntPtr]::Zero
$thread = [IntPtr]::Zero

try {
    $globalAddress = [uint32]$candidate.GlobalAddress
    [byte[]]$stub = Build-ActivationStub $globalAddress

    $remote = [Win32AutoHuntV8]::VirtualAllocEx(
        $hProcess,
        [IntPtr]::Zero,
        [UIntPtr]$stub.Length,
        ($MEM_COMMIT -bor $MEM_RESERVE),
        $PAGE_EXECUTE_READWRITE
    )

    if ($remote -eq [IntPtr]::Zero) {
        throw ("VirtualAllocEx failed. Win32=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }

    [IntPtr]$written = [IntPtr]::Zero
    $ok = [Win32AutoHuntV8]::WriteProcessMemory(
        $hProcess,
        $remote,
        $stub,
        [IntPtr]$stub.Length,
        [ref]$written
    )

    if (-not $ok -or $written.ToInt64() -ne $stub.Length) {
        throw ("WriteProcessMemory failed. Win32=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }

    [uint32]$threadId = 0

    $thread = [Win32AutoHuntV8]::CreateRemoteThread(
        $hProcess,
        [IntPtr]::Zero,
        [UIntPtr]::Zero,
        $remote,
        [IntPtr]::Zero,
        0,
        [ref]$threadId
    )

    if ($thread -eq [IntPtr]::Zero) {
        throw ("CreateRemoteThread failed. Win32=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }

    $wait = [Win32AutoHuntV8]::WaitForSingleObject($thread, 5000)

    if ($wait -ne $WAIT_OBJECT_0) {
        throw ("Activation thread did not finish normally. WaitResult=0x" + $wait.ToString("X8"))
    }

    [uint32]$exitCode = 0
    if (-not [Win32AutoHuntV8]::GetExitCodeThread($thread, [ref]$exitCode)) {
        throw ("GetExitCodeThread failed. Win32=" + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }

    Start-Sleep -Milliseconds 300

    $activeAfterBytes = Read-RemoteBytes $hProcess ([UInt64]$candidate.Object + [UInt64]$ActiveByteOffset) 1

    if ($null -eq $activeAfterBytes) {
        throw "Could not read active state after activation."
    }

    $activeAfter = [int]$activeAfterBytes[0]
    Info ("active after: " + $activeAfter)

    if ($activeAfter -ne 1) {
        throw ("Native Activate(true) returned, but the root active byte is " + $activeAfter + " instead of 1.")
    }

    Ok "Native PledgeNoticeUI Activate(true) call completed."
    Write-Host ""
    Write-Host "LOOK AT THE GAME WINDOW NOW." -ForegroundColor Cyan
    Write-Host "If AutoHuntSettingsWindow appears, the remaining blocker is confirmed as startup activation/toggle wiring." -ForegroundColor Cyan
}
finally {
    if ($thread -ne [IntPtr]::Zero) {
        [void][Win32AutoHuntV8]::CloseHandle($thread)
    }

    if ($remote -ne [IntPtr]::Zero) {
        [void][Win32AutoHuntV8]::VirtualFreeEx($hProcess, $remote, [UIntPtr]::Zero, $MEM_RELEASE)
    }

    if ($hProcess -ne [IntPtr]::Zero) {
        [void][Win32AutoHuntV8]::CloseHandle($hProcess)
    }
}
