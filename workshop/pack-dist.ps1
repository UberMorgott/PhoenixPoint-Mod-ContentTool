# Assembles the Steam Workshop content folder at workshop\Dist\.
#
# The uploaded folder is EXACTLY what deploy.ps1 puts into Mods\ContentTool - the build output
# folder, minus the .pdb. It is not a second definition of "what ships": ContentTool.csproj already
# names the shipped files (OutputPath is bin\Release\ContentTool, and every None/CopyToOutputDirectory
# in it is deliberate), so this copies that folder rather than re-listing its contents here.
#
# ONLY ContentTool is ever published to the Workshop. The demo mods are GitHub downloads reached from
# the documentation site; there is no per-demo path here and there must never be one.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $repoRoot 'ContentTool.csproj'
$out      = Join-Path $repoRoot 'bin\Release\ContentTool'
$dist     = Join-Path $PSScriptRoot 'Dist'

Write-Host "Building ContentTool (Release)..." -ForegroundColor Cyan
dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }

if (-not (Test-Path (Join-Path $out 'ContentTool.dll'))) { throw "no ContentTool.dll under $out" }
if (-not (Test-Path (Join-Path $out 'meta.json')))       { throw "no meta.json under $out" }

# Rebuilt from scratch every run: a file left over from an older revision must never ride along
# into an upload.
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Get-ChildItem $out -Recurse -File |
    Where-Object { $_.Extension -ne '.pdb' } |
    ForEach-Object {
        $rel = $_.FullName.Substring($out.Length).TrimStart('\')
        $to  = Join-Path $dist $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $to -Parent) | Out-Null
        Copy-Item $_.FullName $to -Force
    }

Write-Host "`nDist assembled at: $dist" -ForegroundColor Green
Get-ChildItem $dist -Recurse -File |
    ForEach-Object { "  {0}  ({1:N0} B)" -f $_.FullName.Substring($dist.Length).TrimStart('\'), $_.Length } |
    Sort-Object |
    ForEach-Object { Write-Host $_ }
