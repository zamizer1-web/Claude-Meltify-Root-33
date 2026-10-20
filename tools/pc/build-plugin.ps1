<#
.SYNOPSIS
  Builds the Root 33 plugin against the dev copy of Root and installs it into that copy's BepInEx\plugins\Root33.

.DESCRIPTION
  Dev copy only: refuses any folder that is not under D:\Mods (and anything inside a Steam library), so the creator's
  real Root install is never touched.

  1. Finds the game in -RootDir (or one folder down, in -RootDir\Root) by its BepInEx folder.
  2. Checks the plugin stubs are up to date with the sheets (python -I tools\gen_plugin_stubs.py --check), if Python is here.
  3. dotnet build src\Root33.Plugin -c Release -p:RootDir=<game> (BepInEx 6 IL2CPP; -Mono builds the BepInEx 5 entry).
  4. Copies Root33.Plugin.dll, Root33.Core.dll and Root33.Host.dll (and their .pdb files) to <game>\BepInEx\plugins\Root33\.

  Then start the dev copy: BepInEx\LogOutput.log shows "Root 33 loaded (core ..., sheets ... rows)", F8 toggles the
  panel, F9 writes BepInEx\root33-probe.json (docs\PLUGIN-PLAN.md, M0).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\pc\build-plugin.ps1
  powershell -ExecutionPolicy Bypass -File tools\pc\build-plugin.ps1 -RootDir D:\Mods\Root33-dev -Configuration Debug
#>
param(
  [string]$RootDir = "D:\Mods\Root33-dev",
  [string]$Configuration = "Release",
  [switch]$Mono
)

$ErrorActionPreference = "Stop"
# A reused MSBuild node outlives the build and can hold files and locks; build without one.
$env:MSBUILDDISABLENODEREUSE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Assert-DevCopyPath([string]$path, [string]$what) {
  $full = [System.IO.Path]::GetFullPath($path).TrimEnd('\')
  $ok = $full.StartsWith("D:\Mods\", [System.StringComparison]::OrdinalIgnoreCase) -and $full.Length -gt "D:\Mods\".Length
  if (-not $ok) { throw "Refusing $what '$full': Root 33 builds and installs only into the dev copy under D:\Mods (never the Steam install)." }
  if ($full -match '\\steamapps\\') { throw "Refusing $what '$full': it is inside a Steam library." }
  return $full
}

$RootDir = Assert-DevCopyPath $RootDir "RootDir"
$Game = $RootDir
if (-not (Test-Path -LiteralPath (Join-Path $Game "BepInEx")) -and (Test-Path -LiteralPath (Join-Path $RootDir "Root\BepInEx"))) {
  $Game = Join-Path $RootDir "Root"
}
$Core = Join-Path $Game "BepInEx\core"
if (-not (Test-Path -LiteralPath $Core)) { throw "No BepInEx\core in '$Game'. Install BepInEx into the dev copy first (HANDOFF.md step 3)." }
if ($Mono) {
  if (-not (Test-Path -LiteralPath (Join-Path $Core "BepInEx.dll"))) { throw "-Mono: no BepInEx 5 (BepInEx\core\BepInEx.dll) in '$Game'." }
} else {
  if (-not (Test-Path -LiteralPath (Join-Path $Core "BepInEx.Unity.IL2CPP.dll"))) {
    throw "No BepInEx 6 IL2CPP (BepInEx\core\BepInEx.Unity.IL2CPP.dll) in '$Game'. For a Mono build of Root use -Mono."
  }
  if (-not (Test-Path -LiteralPath (Join-Path $Game "BepInEx\interop"))) {
    throw "No BepInEx\interop in '$Game' yet: start the dev copy of Root once, so BepInEx 6 generates Root's interop assemblies."
  }
}
Write-Host "Root 33: building against the dev copy at $Game"

# The generated stubs must match the sheets (tools\gen_plugin_stubs.py writes them).
$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) { $py = Get-Command py -ErrorAction SilentlyContinue }
if ($py) {
  & $py.Source -I (Join-Path $Repo "tools\gen_plugin_stubs.py") --check
  if ($LASTEXITCODE -ne 0) { throw "The plugin stubs are out of date: run  python -I tools\gen_plugin_stubs.py  and build again." }
} else {
  Write-Warning "Python not found: skipped the stub check (python -I tools\gen_plugin_stubs.py --check)."
}

$project = Join-Path $Repo "src\Root33.Plugin\Root33.Plugin.csproj"
$props = @("-p:RootDir=$Game")
if ($Mono) { $props += "-p:RootMono=true" }
& dotnet build $project -c $Configuration -nologo -nodeReuse:false @props
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }

$out = Join-Path $Repo "src\Root33.Plugin\bin\$Configuration"
$dest = Assert-DevCopyPath (Join-Path $Game "BepInEx\plugins\Root33") "install folder"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($name in @("Root33.Plugin", "Root33.Core", "Root33.Host")) {
  $dll = Join-Path $out "$name.dll"
  if (-not (Test-Path -LiteralPath $dll)) { throw "Build output is missing $dll" }
  Copy-Item -LiteralPath $dll -Destination $dest -Force
  $pdb = Join-Path $out "$name.pdb"
  if (Test-Path -LiteralPath $pdb) { Copy-Item -LiteralPath $pdb -Destination $dest -Force }
}
Write-Host "Root 33: installed Root33.Plugin.dll, Root33.Core.dll and Root33.Host.dll into $dest"
Write-Host "Next: start the dev copy, then check BepInEx\LogOutput.log for 'Root 33 loaded', F8 (panel) and F9 (BepInEx\root33-probe.json)."
