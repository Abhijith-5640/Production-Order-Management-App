<#
.SYNOPSIS
  Full build-and-package pipeline: cleans, builds the client, publishes the API
  and updater helper, writes version.json, then creates the ZIP + SHA256 artefacts.

USAGE (from project root):
  npm run publish:full

  or manually:
  powershell -ExecutionPolicy Bypass -File scripts/publish-helper.ps1
#>

$ErrorActionPreference = 'Stop'

# ── Config ────────────────────────────────────────────────────────────────────
$repoRoot       = Resolve-Path (Join-Path $PSScriptRoot "..")
$apiProj        = Join-Path $repoRoot "src/NexusProd.Api/NexusProd.Api.csproj"
$helperProj     = Join-Path $repoRoot "src/NexusProd.Updater.Helper/NexusProd.Updater.Helper.csproj"
$clientDir      = Join-Path $repoRoot "client"
$apiPublishDir  = Join-Path $repoRoot "src/NexusProd.Api/bin/Release/net8.0/win-x64/publish"
$helperExeName  = "NexusProd.exe"
$versionJson    = Join-Path $apiPublishDir "version.json"
$timestamp      = Get-Date -Format "yyyyMMdd-HHmmss"
$zipName        = "nexusprod-qa-${timestamp}.zip"

# ── Helper: read version from package.json ────────────────────────────────────
function Get-AppVersion {
    $pkgPath = Join-Path $repoRoot "package.json"
    $pkg     = Get-Content $pkgPath -Raw | ConvertFrom-Json
    return $pkg.version
}

# ══════════════════════════════════════════════════════════════════════════════
# [1/6] Clean old build outputs
# ══════════════════════════════════════════════════════════════════════════════
Write-Host "`n[1/6] Cleaning old build outputs…"
$pathsToClean = @(
    (Join-Path $repoRoot "src/NexusProd.Api/bin/Release"),
    (Join-Path $repoRoot "src/NexusProd.Api/obj"),
    (Join-Path $repoRoot "src/NexusProd.Updater.Helper/bin"),
    (Join-Path $repoRoot "src/NexusProd.Updater.Helper/obj"),
    (Join-Path $clientDir "node_modules/.cache")
)
foreach ($p in $pathsToClean) {
    if (Test-Path $p) { Remove-Item -Path $p -Recurse -Force }
}
Write-Host "  Clean done."

# ══════════════════════════════════════════════════════════════════════════════
# [2/6] Build client SPA
# ══════════════════════════════════════════════════════════════════════════════
Write-Host "`n[2/6] Building client SPA…"
Push-Location $clientDir
npm run build
Pop-Location
Write-Host "  Client build done."

# ══════════════════════════════════════════════════════════════════════════════
# [3/6] Publish API
# ══════════════════════════════════════════════════════════════════════════════
# [3a/7] Build API
Write-Host "`n[3a/7] Building API…"
dotnet build $apiProj -c Release -r win-x64 --no-incremental
Write-Host "  API build done."


Write-Host "`n[3/6] Publishing API…"
dotnet publish $apiProj -c Release -r win-x64 -p:PublishSingleFile=true
Write-Host "  API publish done."

# ══════════════════════════════════════════════════════════════════════════════
# [4/6] Publish updater helper and copy into API publish directory
# ══════════════════════════════════════════════════════════════════════════════
Write-Host "`n[4/6] Publishing updater helper…"
dotnet publish $helperProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

$helperExeSrc = Join-Path $repoRoot "src/NexusProd.Updater.Helper/bin/Release/net8.0/win-x64/publish/$helperExeName"
if (-not (Test-Path $helperExeSrc)) {
    throw "Helper exe not found at $helperExeSrc -- publish may have failed."
}
Copy-Item -Path $helperExeSrc -Destination $apiPublishDir -Force
Write-Host "  Helper copied."

# ══════════════════════════════════════════════════════════════════════════════
# [5/6] Write version.json
# ══════════════════════════════════════════════════════════════════════════════
Write-Host "`n[5/6] Writing version.json…"
$version = Get-AppVersion
@{ version = $version } | ConvertTo-Json | Set-Content -Path $versionJson -Encoding UTF8
Write-Host "  Version: $version"

# ══════════════════════════════════════════════════════════════════════════════
# [6/6] Create ZIP + SHA256
# ══════════════════════════════════════════════════════════════════════════════
Write-Host "`n[6/6] Packaging artefacts…"
Push-Location $apiPublishDir
Compress-Archive -Path * -DestinationPath $zipName -Force
$hash = (Get-FileHash $zipName -Algorithm SHA256).Hash
$hash | Out-File -FilePath "${zipName}.sha256" -Encoding ASCII
Pop-Location

$zipPath = Join-Path $apiPublishDir $zipName
Write-Host "  ZIP:   $zipPath"
Write-Host "  SHA256: $hash"

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host "`n===== Publish Complete ====="
Write-Host "  Version:    $version"
Write-Host "  ZIP:        $zipName"
Write-Host "  SHA256:     ${zipName}.sha256"
Write-Host "`nFiles in publish directory:"
Get-ChildItem $apiPublishDir | Format-Table Name, @{ N='SizeMB'; E={ '{0:N1} MB' -f ($_.Length / 1MB) } } -AutoSize
