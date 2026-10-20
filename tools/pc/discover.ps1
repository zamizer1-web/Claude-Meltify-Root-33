<#
.SYNOPSIS
  Runs Root33.Discover over the dev copy of Root: writes types.json and report.md (and copies the F9 probe file) into
  D:\Mods\Root33-dev\discover.

.DESCRIPTION
  Read-only for the game: Root33.Discover reads Root's assemblies as metadata (System.Reflection.MetadataLoadContext),
  nothing in them runs. Under BepInEx 6 IL2CPP it reads <game>\BepInEx\interop (with BepInEx\core for references); for a
  Mono build, <game>\Root_Data\Managed. Both folders must be under D:\Mods.

  report.md lists the types and members named after Root concepts (clearing, faction, battle, birdsong, dominance, ...)
  and the likely game-flow types; types.json has every type, one per line. They hold Root's code structure, so they stay
  in the dev folder on D: and do not go into the repo (docs\PLUGIN-PLAN.md, M1).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\pc\discover.ps1
  powershell -ExecutionPolicy Bypass -File tools\pc\discover.ps1 -All
#>
param(
  [string]$RootDir = "D:\Mods\Root33-dev",
  [string]$OutDir = "D:\Mods\Root33-dev\discover",
  [switch]$All
)

$ErrorActionPreference = "Stop"
$env:MSBUILDDISABLENODEREUSE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Assert-DevCopyPath([string]$path, [string]$what) {
  $full = [System.IO.Path]::GetFullPath($path).TrimEnd('\')
  $ok = $full.StartsWith("D:\Mods\", [System.StringComparison]::OrdinalIgnoreCase) -and $full.Length -gt "D:\Mods\".Length
  if (-not $ok) { throw "Refusing $what '$full': Root 33 reads only the dev copy under D:\Mods (never the Steam install)." }
  if ($full -match '\\steamapps\\') { throw "Refusing $what '$full': it is inside a Steam library." }
  return $full
}

$RootDir = Assert-DevCopyPath $RootDir "RootDir"
$OutDir = Assert-DevCopyPath $OutDir "OutDir"
$Game = $RootDir
if (-not (Test-Path -LiteralPath (Join-Path $Game "BepInEx")) -and (Test-Path -LiteralPath (Join-Path $RootDir "Root\BepInEx"))) {
  $Game = Join-Path $RootDir "Root"
}

$interop = Join-Path $Game "BepInEx\interop"
$managed = Join-Path $Game "Root_Data\Managed"
if (Test-Path -LiteralPath $interop) {
  $folder = $interop
  Write-Host "Root 33: IL2CPP build, reading $folder (references from BepInEx\core)"
} elseif (Test-Path -LiteralPath (Join-Path $managed "Assembly-CSharp.dll")) {
  $folder = $managed
  Write-Host "Root 33: Mono build, reading $folder"
} else {
  throw "Neither $interop nor a Mono $managed\Assembly-CSharp.dll exists. Start the dev copy once with BepInEx 6 so it generates BepInEx\interop."
}

$tool = Join-Path $Repo "tools\Root33.Discover"
& dotnet build $tool -c Release -nologo -v q -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "Building Root33.Discover failed (exit $LASTEXITCODE)." }
$runArgs = @("run", "--no-build", "--project", $tool, "-c", "Release", "--", $folder, $OutDir)
if ($All) { $runArgs += "--all" }
& dotnet @runArgs
if ($LASTEXITCODE -ne 0) { throw "Root33.Discover failed (exit $LASTEXITCODE)." }

$probe = Join-Path $Game "BepInEx\root33-probe.json"
if (Test-Path -LiteralPath $probe) {
  Copy-Item -LiteralPath $probe -Destination $OutDir -Force
  Write-Host "Root 33: copied the F9 probe file next to the report."
} else {
  Write-Host "Root 33: no BepInEx\root33-probe.json yet (press F9 in the dev copy with ProbeMode on to write one)."
}
Write-Host "Next: read $(Join-Path $OutDir 'report.md') (docs\PLUGIN-PLAN.md, M1)."
