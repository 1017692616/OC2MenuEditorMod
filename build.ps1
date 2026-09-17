param(
  [switch]$NoInstall,
  [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$game = "F:\SteamLibrary\steamapps\common\Overcooked! 2"
$managed = Join-Path $game "Overcooked2_Data\Managed"
$core = Join-Path $game "BepInEx\core"
$out = Join-Path $PSScriptRoot "dist"
$builtDll = Join-Path $out "OC2MenuEditorMod.dll"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$sdk = & dotnet --list-sdks | Select-Object -First 1
$sdkVersion = ($sdk -split "\s+")[0]
$csc = Join-Path "C:\Program Files\dotnet\sdk\$sdkVersion\Roslyn\bincore" "csc.dll"
$refs = @(
  (Join-Path $managed "mscorlib.dll"), (Join-Path $managed "System.dll"), (Join-Path $managed "System.Core.dll"),
  (Join-Path $managed "UnityEngine.dll"), (Join-Path $managed "UnityEngine.CoreModule.dll"),
  (Join-Path $managed "UnityEngine.IMGUIModule.dll"), (Join-Path $managed "UnityEngine.UI.dll"),
  (Join-Path $managed "UnityEngine.UIModule.dll"), (Join-Path $managed "UnityEngine.TextRenderingModule.dll"),
  (Join-Path $managed "Assembly-CSharp.dll"), (Join-Path $managed "Assembly-CSharp-firstpass.dll"),
  (Join-Path $core "BepInEx.dll"), (Join-Path $core "0Harmony.dll")
)
$args = @($csc, "-noconfig", "-nostdlib+", "-target:library", "-langversion:7.3", "-codepage:65001", "-optimize+", "-out:$builtDll")
foreach ($ref in $refs) { $args += "-reference:$ref" }
foreach ($sourceFile in (Get-ChildItem -Path (Join-Path $PSScriptRoot "src") -Filter "*.cs" | Sort-Object Name)) { $args += $sourceFile.FullName }
& dotnet $args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Write-Host "Built $builtDll"
if (-not $NoInstall) {
  $pluginDir = Join-Path $game "BepInEx\plugins\OC2MenuEditorMod"
  New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
  Copy-Item -LiteralPath $builtDll -Destination (Join-Path $pluginDir "OC2MenuEditorMod.dll") -Force
}
if (-not $NoLaunch) { Start-Process "steam://rungameid/728880" }
