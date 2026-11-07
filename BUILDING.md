# Building Custom Paintings

## Requirements

- Windows with PowerShell 7 and Windows PowerShell 5.1.
- .NET SDK 10 (the checks target .NET 10) and .NET Framework 4.7.1 reference assemblies, available through the developer pack or compatible SDK tooling.
- A local Sailwind installation and BepInExPack 5.4.2305, including BepInEx's `Mono.Cecil.dll`, `MonoMod.Utils.dll` and `MonoMod.RuntimeDetour.dll` for the binding checks.

Game and loader assemblies are build references only. They are not included in this repository or its packages.

## Build

From the project directory, run:

```powershell
pwsh -File ./Build.ps1 -GameManagedDir 'C:\Games\Sailwind\Sailwind_Data\Managed' -ReferenceDir 'C:\Games\Sailwind\BepInEx\core'
```

Use the paths for your own installation or mod profile. Alternatively, place `BepInEx.dll` and `0Harmony.dll` in `.local/references/` and omit `-ReferenceDir`. Without either, the build uses the Sailwind Mod Synchronizer default pack's `BepInEx/core` folder.

The Release output is `bin/Release/net471/CustomPaintings.dll`.

## Test

From the project directory:

```powershell
pwsh -File ./Test.ps1 -GameManagedDir 'C:\Games\Sailwind\Sailwind_Data\Managed' -ReferenceDir 'C:\Games\Sailwind\BepInEx\core'
```

Each suite runs in a separate process and any failure makes the command fail.

### Coverage

| Suite | Behavior protected |
| --- | --- |
| `logic` | Pictures keep their size unless larger than the limit and get compression-friendly sizes. Fill covers every frame shape without stretching, Fit shows the whole picture undistorted, and Stretch samples the whole picture. Every native frame mesh has its canvas shape, and border colours are read safely. Saved picture, mode and border choices round-trip with defaults left out, unknown modes show as Fill, a different painting under the same ID gets nothing, and unreadable data is left unwritten until a new choice replaces it. Folders list subfolders first in natural name order without reading deeper, search covers only the open folder, and paging rounds up. |
| production build | The plugin compiles against the installed game and loader assemblies. |
| `tests/binding` | Every Harmony patch generates a replacement against the installed game, and every game member the plugin reads still exists, including the hover prompt's controls text and R icon. No game code is executed. |

These checks do not cover picture loading, the picture menu, look prompts, frame appearance or save and load in the running game. Those need an in-game test.

## Package

Run `Package.ps1` under PowerShell 7 with the same reference arguments. It builds the DLL, checks the manifest and release notes, and writes a plugin ZIP under `artifacts/`. Add `-SkipBuild` to package an already built Release DLL. The package includes `icon.png` when the file exists.
