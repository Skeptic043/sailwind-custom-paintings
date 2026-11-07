$ErrorActionPreference = 'Stop'
& dotnet build (Join-Path $PSScriptRoot 'Logic.csproj') -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Custom Paintings logic checks failed to build.' }
& (Join-Path $PSScriptRoot 'bin/Release/net10.0/Logic.exe')
if ($LASTEXITCODE -ne 0) { throw 'Custom Paintings logic checks failed.' }
