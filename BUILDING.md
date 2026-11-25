# Building Sailwind HelmVR

## Requirements

- Windows with PowerShell 7 and the .NET 10 SDK (the tests target .NET 10).
- A local Sailwind installation with BepInEx 5.

Game and loader assemblies are build references only. They are not included in this repository or its packages.

Two MSBuild properties point at those folders:

- `SailwindManaged` is the game's `Sailwind_Data\Managed` folder. Default: `C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed`
- `BepInExCore` is the `BepInEx\core` folder. Default: `%LOCALAPPDATA%\SailwindModSynchronizer\packs\default\instance\BepInEx\core`

## Build

From the repository folder:

```powershell
dotnet build src -c Release -p:SailwindManaged='C:\Games\Sailwind\Sailwind_Data\Managed' -p:BepInExCore='C:\Games\Sailwind\BepInEx\core'
```

Use the paths for your own installation or mod profile. The output is `src/bin/Release/netstandard2.0/HelmVR.dll`.

## Test

```powershell
dotnet run --project tests -c Release -p:SailwindManaged='C:\Games\Sailwind\Sailwind_Data\Managed' -p:BepInExCore='C:\Games\Sailwind\BepInEx\core'
```

The checks confirm that every method HelmVR patches and every game field or method it looks up by name still exists in your installed Sailwind, and that the README only names real settings. Any failure makes the command fail.

## Package

```powershell
pwsh -File ./Package.ps1 -SailwindManaged 'C:\Games\Sailwind\Sailwind_Data\Managed' -BepInExCore 'C:\Games\Sailwind\BepInEx\core'
```

This builds the mod and writes `artifacts/release/HelmVR-<version>.zip` with `BepInEx/plugins/HelmVR/`, `manifest.json`, `README.md`, `CHANGELOG.md` and `LICENSE`.
