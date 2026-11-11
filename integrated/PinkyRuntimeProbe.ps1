if (-not ('PinkyRuntimePipeNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class PinkyRuntimePipeNative {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WaitNamedPipe(string name, uint timeoutMilliseconds);
}
'@
}

function Get-PinkyPipeName([string]$ProfilePath) {
    if ([string]::IsNullOrWhiteSpace($ProfilePath)) { throw 'Pinky profile path is required.' }
    $runDirectory = [IO.Path]::GetFullPath((Join-Path $ProfilePath 'run'))
    $normalized = $runDirectory.ToLowerInvariant()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalized))
    } finally {
        $sha.Dispose()
    }
    $prefix = [BitConverter]::ToString($digest, 0, 8).Replace('-', '').ToLowerInvariant()
    return ('\\.\pipe\pinky-' + $prefix)
}

function Test-PinkyNamedPipe([string]$PipeName, [scriptblock]$Probe = $null) {
    if ($Probe) {
        $result = & $Probe $PipeName
        if ($null -eq $result -or $null -eq $result.Succeeded -or $null -eq $result.ErrorCode) {
            throw 'The Pinky pipe probe returned an invalid result.'
        }
        $succeeded = [bool]$result.Succeeded
        $errorCode = [int]$result.ErrorCode
    } else {
        $succeeded = [PinkyRuntimePipeNative]::WaitNamedPipe($PipeName, [uint32]50)
        $errorCode = if ($succeeded) { 0 } else { [Runtime.InteropServices.Marshal]::GetLastWin32Error() }
    }
    if ($succeeded) { return $true }
    switch ($errorCode) {
        2   { return $false } # ERROR_FILE_NOT_FOUND
        3   { return $false } # ERROR_PATH_NOT_FOUND
        121 { return $true }  # ERROR_SEM_TIMEOUT: pipe exists but did not become available
        231 { return $true }  # ERROR_PIPE_BUSY
        5   { throw [ComponentModel.Win32Exception]::new($errorCode, 'Access denied probing Pinky named pipe.') }
        default { throw [ComponentModel.Win32Exception]::new($errorCode, 'Could not determine Pinky named pipe state.') }
    }
}

function Test-PinkySocket([string]$ProfilePath, [scriptblock]$Probe = $null) {
    if ([string]::IsNullOrWhiteSpace($ProfilePath)) { throw 'Pinky profile path is required.' }
    $legacySocket = Join-Path (Join-Path $ProfilePath 'run') 'pinky.sock'
    try {
        $attributes = [IO.File]::GetAttributes($legacySocket)
        if (($attributes -band [IO.FileAttributes]::Directory) -eq 0) { return $true }
    } catch [IO.FileNotFoundException] {
        # Pinky 19 uses a named pipe; continue with the precise pipe probe.
    } catch [IO.DirectoryNotFoundException] {
        # No legacy runtime directory; continue with the precise pipe probe.
    }
    return Test-PinkyNamedPipe (Get-PinkyPipeName $ProfilePath) $Probe
}
