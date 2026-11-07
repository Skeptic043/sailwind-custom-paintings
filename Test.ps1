#requires -Version 7.0
param(
    [string]$GameManagedDir = 'C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed',
    [string]$ReferenceDir = ''
)

$ErrorActionPreference = 'Stop'
$pwsh = (Get-Process -Id $PID).Path
$failed = [Collections.Generic.List[string]]::new()

Write-Host "`n=== logic ==="
try {
    & $pwsh -NoProfile -File (Join-Path $PSScriptRoot 'tests/logic/Run.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Child process exited with code $LASTEXITCODE." }
    Write-Host 'PASS: logic'
} catch {
    $failed.Add('logic')
    Write-Host "FAIL: logic - $($_.Exception.Message)"
}

Write-Host "`n=== production build ==="
try {
    & $pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Build.ps1') `
        -GameManagedDir $GameManagedDir -ReferenceDir $ReferenceDir
    if ($LASTEXITCODE -ne 0) { throw "Child process exited with code $LASTEXITCODE." }
    Write-Host 'PASS: production build'
} catch {
    $failed.Add('production build')
    Write-Host "FAIL: production build - $($_.Exception.Message)"
}

Write-Host "`n=== native patch binding ==="
if ($failed.Contains('production build')) {
    $failed.Add('native patch binding')
    Write-Host 'FAIL: native patch binding requires a successful current production build.'
} else {
    try {
        $desktop = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
        & $desktop -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests/binding/Run.ps1') `
            -GameManagedDir $GameManagedDir -ReferenceDir $ReferenceDir
        if ($LASTEXITCODE -ne 0) { throw "Child process exited with code $LASTEXITCODE." }
        Write-Host 'PASS: native patch binding'
    } catch {
        $failed.Add('native patch binding')
        Write-Host "FAIL: native patch binding - $($_.Exception.Message)"
    }
}

if ($failed.Count -gt 0) {
    Write-Host "`nFailed: $($failed -join ', ')"
    exit 1
}
Write-Host "`nLogic checks, production build and native patch binding passed."
exit 0
