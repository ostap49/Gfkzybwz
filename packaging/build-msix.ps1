# Збирає Rozkladka.msix для завантаження в Microsoft Store (Store сам підписує пакет).
# Параметри Identity/Publisher — з Partner Center → Product management → Product identity.
param(
  [Parameter(Mandatory)] [string] $Version,             # 0.2.0 → 0.2.0.0
  [string] $IdentityName = "Rozkladka.Dev",
  [string] $Publisher = "CN=Rozkladka-Dev",
  [string] $PublisherDisplayName = "ostap49",
  [string] $OutDir = "publish"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$layout = Join-Path $root "$OutDir\msix-layout"
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }

# Для MSIX — звичайна (не single-file) публікація: без розпакування в тимчасову теку.
dotnet publish "$root\src\Rozkladka" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $layout
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item "$PSScriptRoot\msix\Assets" "$layout\Assets" -Recurse
$v = $Version; while (($v.Split('.')).Count -lt 4) { $v = "$v.0" }
(Get-Content "$PSScriptRoot\msix\AppxManifest.xml" -Raw -Encoding UTF8).
  Replace('$IDENTITY_NAME$', $IdentityName).
  Replace('$PUBLISHER_DISPLAY_NAME$', $PublisherDisplayName).
  Replace('$PUBLISHER$', $Publisher).
  Replace('$VERSION$', $v) | Set-Content "$layout\AppxManifest.xml" -Encoding UTF8

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
  Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw "makeappx.exe не знайдено (потрібен Windows SDK)" }
$out = Join-Path $root "$OutDir\Rozkladka-Store.msix"
& $makeappx.FullName pack /o /d $layout /p $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "MSIX: $out"
