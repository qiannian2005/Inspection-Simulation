param([switch]$NoBuild, [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'HaspDemo.UI\HaspDemo.UI.csproj'
$exe = Join-Path $PSScriptRoot 'HaspDemo.UI\bin\x64\Debug\net8.0-windows\HaspDemo.UI.exe'
if (-not $env:HALCONROOT) { throw 'HALCONROOT is missing. Install HALCON and reopen the terminal.' }
$halconBin = Join-Path $env:HALCONROOT 'bin\x64-win64'
if (-not (Test-Path (Join-Path $halconBin 'halcon.dll'))) { throw "HALCON runtime not found: $halconBin" }
$env:PATH = "$halconBin;$env:PATH"
if (-not $NoBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Visual Studio 2022 / MSBuild is required by the existing COM reference.' }
    $installation = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -format json -utf8 | ConvertFrom-Json
    $msbuild = Join-Path $installation[0].installationPath 'MSBuild\Current\Bin\MSBuild.exe'
    & $msbuild $project /restore /p:Configuration=Debug /p:Platform=x64 /nologo /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
}
if ($BuildOnly) { return }
if (-not (Test-Path $exe)) { throw 'Executable not found. Run without -NoBuild first.' }
Start-Process -FilePath $exe -ArgumentList '--simulate' -WorkingDirectory (Split-Path $exe)
