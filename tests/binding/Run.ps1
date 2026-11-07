#requires -PSEdition Desktop
param(
    [string]$GameManagedDir = 'C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed',
    [string]$ReferenceDir = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if (-not $ReferenceDir) {
    $ReferenceDir = Join-Path $root '.local/references'
    if (-not (Test-Path -LiteralPath (Join-Path $ReferenceDir 'BepInEx.dll'))) {
        $ReferenceDir = Join-Path $env:LOCALAPPDATA 'SailwindModSynchronizer\packs\default\instance\BepInEx\core'
    }
}
$harmony = $null
try {
    $plugin = Join-Path $root 'bin/Release/net471/CustomPaintings.dll'
    foreach ($path in @((Join-Path $GameManagedDir 'Assembly-CSharp.dll'), (Join-Path $GameManagedDir 'Oculus.VR.dll'),
        (Join-Path $ReferenceDir 'BepInEx.dll'), (Join-Path $ReferenceDir '0Harmony.dll'),
        (Join-Path $ReferenceDir 'Mono.Cecil.dll'), $plugin)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required binding input is missing: $path" }
    }
    foreach ($path in [IO.Directory]::GetFiles($GameManagedDir, 'UnityEngine*.dll')) { [void][Reflection.Assembly]::LoadFrom($path) }
    $oculus = [Reflection.Assembly]::LoadFrom((Join-Path $GameManagedDir 'Oculus.VR.dll'))
    $game = [Reflection.Assembly]::LoadFrom((Join-Path $GameManagedDir 'Assembly-CSharp.dll'))
    foreach ($name in @('BepInEx.dll', '0Harmony.dll')) { [void][Reflection.Assembly]::LoadFrom((Join-Path $ReferenceDir $name)) }
    foreach ($pattern in @('Mono.Cecil*.dll', 'MonoMod*.dll')) {
        foreach ($path in [IO.Directory]::GetFiles($ReferenceDir, $pattern)) { [void][Reflection.Assembly]::LoadFrom($path) }
    }
    $assembly = [Reflection.Assembly]::LoadFrom($plugin)

    # Members the plugin reads directly. A missing one would fail at runtime, not at patch time.
    $all = [Reflection.BindingFlags]'Instance,Static,Public,NonPublic'
    $members = @(
        @('GameState', 'modData'), @('GameState', 'currentlyLoading'), @('GameState', 'inCursorMenu'),
        @('GameState', 'sleeping'), @('GameState', 'inBed'), @('BoatCamera', 'on'),
        @('SaveablePrefab', 'instanceId'), @('SaveablePrefab', 'prefabIndex'),
        @('ShipItem', 'sold'), @('ShipItem', 'nailed'), @('ShipItem', 'name'),
        @('LookUI', 'ClearText'), @('GoPointerButton', 'unclickable'), @('GoPointerButton', 'IsStickyClicked'),
        @('GoPointer', 'GetHeldItem'), @('GoPointer', 'hit'), @('GoPointer', 'pointedAtButton'),
        @('GoPointer', 'currentLookDistance'), @('GoPointer', 'type'),
        @('LookUI', 'controlsText'), @('LookUI', 'ShowRicon'), @('LookUI', 'HideIcons'), @('LookUI', 'pointer'),
        @('Settings', 'controlsTextEnabled'), @('MouseLook', 'ToggleMouseLookAndCursor'), @('Refs', 'SetPlayerControl'),
        @('StartMenu', 'chooseIslandUI'), @('GameState', 'wasInSettingsMenu'), @('GoPointerButton', 'outline'),
        @('Settings', 'VRLookEnabled'), @('Refs', 'observerMirror'))
    foreach ($pair in $members) {
        $type = $game.GetType($pair[0], $true)
        if (@($type.GetMember($pair[1], $all)).Count -eq 0) { throw "Missing native member: $($pair[0]).$($pair[1])" }
        Write-Output "MEMBER PASS: $($pair[0]).$($pair[1])"
    }

    Add-Type -Path (Join-Path $PSScriptRoot 'GenerationOnlyPatcher.cs') -ReferencedAssemblies @(
        [HarmonyLib.Harmony].Assembly.Location,
        [MonoMod.Utils.DynamicMethodDefinition].Assembly.Location,
        (Join-Path $ReferenceDir 'Mono.Cecil.dll'))
    [GenerationOnlyPatcher]::Register()
    $harmony = [HarmonyLib.Harmony]::new('custompaintings.tests.binding')
    $patchClasses = @($assembly.GetTypes() | Where-Object {
        @($_.GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }).Count -gt 0
    } | Sort-Object FullName)
    if ($patchClasses.Count -eq 0) { throw 'No Harmony patch classes were discovered.' }
    foreach ($type in $patchClasses) {
        $patches = $harmony.CreateClassProcessor($type).Patch()
        if ($null -eq $patches -or $patches.Count -eq 0) { throw "Patch produced no binding: $($type.FullName)" }
        Write-Output "IL GENERATION PASS: $($type.FullName) ($($patches.Count) target(s))"
    }
    [GenerationOnlyPatcher]::VerifyInvalidPrefixRejected($harmony)
    Write-Output 'CONTROL PASS: Harmony rejected an invalid prefix argument through the generation-only backend.'
    Write-Output "Generated replacements for $($patchClasses.Count) patch classes. No gameplay target or plugin Awake was invoked."
} catch {
    Write-Error "Native binding failed: $($_.Exception.ToString())" -ErrorAction Continue
    exit 1
} finally {
    if ($null -ne $harmony) {
        try { $harmony.UnpatchSelf() }
        catch { Write-Error "Failed to remove isolated patches: $($_.Exception.Message)" -ErrorAction Continue; exit 1 }
    }
}
exit 0
