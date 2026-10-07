<#
.SYNOPSIS
  Read-only scan for Root 33: finds Root and Clair Obscur: Expedition 33 on this PC and reports how each is built.

.DESCRIPTION
  Looks through every Steam library folder (and any paths you pass) and writes pc-report.json next to this
  script's working directory. It changes nothing on the PC.

  Reports, for Root (Steam app 965580): install folder, build id, Unity version, Mono or IL2CPP (which decides
  BepInEx 5 vs BepInEx 6 IL2CPP / MelonLoader), managed assembly names, installed DLC depots, existing mod loaders.
  For Clair Obscur (Steam app 1903340): install folder, build id, engine version, the Paks folder layout.
  Also free space per drive, so big files can go to D:.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\pc\find-games.ps1
  powershell -ExecutionPolicy Bypass -File tools\pc\find-games.ps1 -RootPath "E:\Games\Root" -E33Path "E:\Games\Expedition 33"
#>
param(
  [string]$SteamPath = "",
  [string]$RootPath = "",
  [string]$E33Path = "",
  [string]$OutFile = "pc-report.json"
)

$ErrorActionPreference = "Continue"
$RootAppId = "965580"
$E33AppId = "1903340"

function Get-SteamPath {
  foreach ($key in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
    try {
      $p = Get-ItemProperty -Path $key -ErrorAction Stop
      if ($p.SteamPath) { return ($p.SteamPath -replace "/", "\") }
      if ($p.InstallPath) { return $p.InstallPath }
    } catch { }
  }
  return $null
}

function Get-SteamLibraries([string]$steamPath) {
  $libs = New-Object System.Collections.Generic.List[string]
  if (-not $steamPath) { return $libs }
  $libs.Add($steamPath)
  $vdf = Join-Path $steamPath "steamapps/libraryfolders.vdf"
  if (Test-Path $vdf) {
    foreach ($line in Get-Content -LiteralPath $vdf) {
      if ($line -match '^\s*"path"\s*"(.+)"\s*$') {
        $p = $Matches[1] -replace "\\\\", "\"
        if (-not $libs.Contains($p)) { $libs.Add($p) }
      }
    }
  }
  return $libs
}

function Get-VdfChildKeys([string]$text, [string]$blockName) {
  # Returns the keys of the sub-blocks directly inside "<blockName>" { ... }, tracking brace depth.
  $keys = @()
  $start = $text.IndexOf('"' + $blockName + '"')
  if ($start -lt 0) { return $keys }
  $open = $text.IndexOf('{', $start)
  if ($open -lt 0) { return $keys }
  $depth = 0; $i = $open; $lastKey = $null
  while ($i -lt $text.Length) {
    $c = $text[$i]
    if ($c -eq '"') {
      $end = $text.IndexOf('"', $i + 1)
      if ($end -lt 0) { break }
      if ($depth -eq 1) { $lastKey = $text.Substring($i + 1, $end - $i - 1) }
      $i = $end + 1; continue
    }
    if ($c -eq '{') { $depth++; if ($depth -eq 2 -and $lastKey) { $keys += $lastKey } }
    elseif ($c -eq '}') { $depth--; if ($depth -eq 0) { break } }
    $i++
  }
  return $keys
}

function Read-AppManifest([string]$lib, [string]$appId) {
  $acf = Join-Path $lib "steamapps/appmanifest_$appId.acf"
  if (-not (Test-Path $acf)) { return $null }
  $text = Get-Content -LiteralPath $acf -Raw
  $info = [ordered]@{ library = $lib; manifest = $acf }
  foreach ($k in @("name", "installdir", "buildid", "SizeOnDisk", "LastUpdated", "StateFlags")) {
    if ($text -match "`"$k`"\s+`"([^`"]*)`"") { $info[$k] = $Matches[1] }
  }
  $info["installedDepots"] = @(Get-VdfChildKeys $text "InstalledDepots")
  $info["folder"] = Join-Path $lib ("steamapps/common/" + $info["installdir"])
  return $info
}

function Get-FileVersionSafe([string]$path) {
  if (-not (Test-Path -LiteralPath $path)) { return $null }
  try { return (Get-Item -LiteralPath $path).VersionInfo.FileVersion } catch { return $null }
}

function Describe-Root([string]$folder) {
  $r = [ordered]@{ folder = $folder; exists = (Test-Path -LiteralPath $folder) }
  if (-not $r.exists) { return $r }
  $exe = Get-ChildItem -LiteralPath $folder -Filter *.exe -File | Where-Object { $_.Name -notmatch "UnityCrashHandler|unins" } | Select-Object -First 1
  $r["exe"] = if ($exe) { $exe.Name } else { $null }
  $dataDir = Get-ChildItem -LiteralPath $folder -Directory | Where-Object { $_.Name -like "*_Data" } | Select-Object -First 1
  $r["dataFolder"] = if ($dataDir) { $dataDir.Name } else { $null }
  $r["unityPlayerVersion"] = Get-FileVersionSafe (Join-Path $folder "UnityPlayer.dll")
  $il2cpp = Test-Path -LiteralPath (Join-Path $folder "GameAssembly.dll")
  $managed = if ($dataDir) { Join-Path $dataDir.FullName "Managed" } else { $null }
  $mono = $managed -and (Test-Path -LiteralPath (Join-Path $managed "Assembly-CSharp.dll"))
  $r["scripting"] = if ($il2cpp) { "il2cpp" } elseif ($mono) { "mono" } else { "unknown" }
  $r["loaderSuggestion"] = if ($il2cpp) { "bepinex-6-il2cpp or melonloader" } elseif ($mono) { "bepinex-5" } else { "unknown" }
  if ($mono) {
    $r["managedAssemblies"] = @(Get-ChildItem -LiteralPath $managed -Filter *.dll -File | ForEach-Object { $_.Name })
  }
  if ($il2cpp -and $dataDir) {
    $meta = Join-Path $dataDir.FullName "il2cpp_data/Metadata/global-metadata.dat"
    $r["il2cppMetadata"] = (Test-Path -LiteralPath $meta)
  }
  if ($dataDir) {
    $sa = Join-Path $dataDir.FullName "StreamingAssets"
    if (Test-Path -LiteralPath $sa) {
      $r["streamingAssetsTop"] = @(Get-ChildItem -LiteralPath $sa | Select-Object -First 40 | ForEach-Object { $_.Name })
    }
  }
  $r["existingLoaders"] = [ordered]@{
    bepinex = (Test-Path -LiteralPath (Join-Path $folder "BepInEx"))
    melonloader = (Test-Path -LiteralPath (Join-Path $folder "MelonLoader"))
    doorstop = (Test-Path -LiteralPath (Join-Path $folder "winhttp.dll")) -or (Test-Path -LiteralPath (Join-Path $folder "version.dll"))
  }
  $r["sizeGB"] = [math]::Round(((Get-ChildItem -LiteralPath $folder -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1GB), 2)
  return $r
}

function Describe-E33([string]$folder) {
  $r = [ordered]@{ folder = $folder; exists = (Test-Path -LiteralPath $folder) }
  if (-not $r.exists) { return $r }
  $ship = Get-ChildItem -LiteralPath $folder -Recurse -Filter "*-Win64-Shipping.exe" -File -ErrorAction SilentlyContinue | Select-Object -First 1
  $r["shippingExe"] = if ($ship) { $ship.FullName.Substring($folder.Length).TrimStart("\", "/") } else { $null }
  $r["engineFileVersion"] = if ($ship) { Get-FileVersionSafe $ship.FullName } else { $null }
  $paks = Get-ChildItem -LiteralPath $folder -Recurse -Directory -Filter "Paks" -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($paks) {
    $r["paksFolder"] = $paks.FullName.Substring($folder.Length).TrimStart("\", "/")
    $r["paks"] = @(Get-ChildItem -LiteralPath $paks.FullName -File | ForEach-Object {
      [ordered]@{ name = $_.Name; sizeMB = [math]::Round($_.Length / 1MB, 1) } })
    $r["hasModsFolder"] = (Test-Path -LiteralPath (Join-Path $paks.FullName "~mods"))
  }
  $r["ue4ss"] = [bool](Get-ChildItem -LiteralPath $folder -Recurse -Filter "UE4SS*.dll" -File -ErrorAction SilentlyContinue | Select-Object -First 1)
  return $r
}

$report = [ordered]@{
  generated = (Get-Date).ToString("s")
  os = [System.Environment]::OSVersion.VersionString
  steamPath = $null
  libraries = @()
  root = $null
  e33 = $null
  drives = @()
  notes = @()
}

$steam = if ($SteamPath) { $SteamPath } else { Get-SteamPath }
$report.steamPath = $steam
$libs = Get-SteamLibraries $steam
$report.libraries = @($libs)

$rootManifest = $null; $e33Manifest = $null
foreach ($lib in $libs) {
  if (-not $rootManifest) { $rootManifest = Read-AppManifest $lib $RootAppId }
  if (-not $e33Manifest) { $e33Manifest = Read-AppManifest $lib $E33AppId }
}

$rootFolder = if ($RootPath) { $RootPath } elseif ($rootManifest) { $rootManifest.folder } else { $null }
$e33Folder = if ($E33Path) { $E33Path } elseif ($e33Manifest) { $e33Manifest.folder } else { $null }

if ($rootFolder) { $report.root = [ordered]@{ steam = $rootManifest; build = (Describe-Root $rootFolder) } }
else { $report.notes += "Root (Steam app $RootAppId) was not found in any Steam library. Pass -RootPath if it is installed elsewhere." }
if ($e33Folder) { $report.e33 = [ordered]@{ steam = $e33Manifest; build = (Describe-E33 $e33Folder) } }
else { $report.notes += "Clair Obscur: Expedition 33 (Steam app $E33AppId) was not found in any Steam library. Pass -E33Path if it is installed elsewhere (e.g. Xbox app or Epic)." }

$report.drives = @(Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Used -ne $null } | ForEach-Object {
  [ordered]@{ name = $_.Name; freeGB = [math]::Round($_.Free / 1GB, 1); usedGB = [math]::Round($_.Used / 1GB, 1) } })
if (-not (Get-PSDrive -Name D -ErrorAction SilentlyContinue)) { $report.notes += "There is no D: drive on this PC." }

$json = $report | ConvertTo-Json -Depth 8
Set-Content -LiteralPath $OutFile -Value $json -Encoding UTF8

Write-Host "Root 33 PC scan (read-only) - report written to $OutFile"
if ($report.root) {
  $b = $report.root.build
  Write-Host ("  Root:        {0}" -f $b.folder)
  Write-Host ("               Unity {0}, scripting {1} -> loader {2}" -f $b.unityPlayerVersion, $b.scripting, $b.loaderSuggestion)
} else { Write-Host "  Root:        not found" }
if ($report.e33) {
  $b = $report.e33.build
  Write-Host ("  Clair Obscur: {0}" -f $b.folder)
  Write-Host ("               engine file version {0}, paks folder {1}" -f $b.engineFileVersion, $b.paksFolder)
} else { Write-Host "  Clair Obscur: not found" }
foreach ($n in $report.notes) { Write-Host "  Note: $n" }
