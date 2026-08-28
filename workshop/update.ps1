<#
.SYNOPSIS
    SteamCMD update path for the ContentTool Steam Workshop item.

.DESCRIPTION
    Run this on the machine logged into the account that OWNS the item. Steps:
      1. Locate steamcmd.exe (PATH or the usual places); print an install hint and stop if absent.
      2. Refuse unless contenttool.vdf carries a real publishedfileid. The first publish creates the
         item and there is nothing to update before it - see WORKSHOP.md.
      3. Refuse unless the preview image exists (<= 1 MB, 1024x1024, JPG or PNG). An upload with no
         preview is not worth taking back.
      4. Run pack-dist.ps1 to rebuild workshop\Dist.
      5. Stamp the change note into the vdf and run
             steamcmd +login <user> +workshop_build_item <abs vdf> +quit

    ONE item is ever published: ContentTool. The demo mods are GitHub downloads reached from the
    documentation site - there is no per-demo path here and there must never be one.

    SECURITY: no credential is stored or hardcoded. The account name is a parameter; SteamCMD
    prompts for the password and the Steam Guard / 2FA code in its own console.

.EXAMPLE
    .\workshop\update.ps1 -ChangeNote "1.0.1 - fixes the sound bake on a fresh install" -SteamUser myname
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ChangeNote,
    [string] $SteamUser,
    [string] $SteamCmd
)
$ErrorActionPreference = 'Stop'

$here     = $PSScriptRoot
$repoRoot = Split-Path -Parent $here
$vdf      = Join-Path $here 'contenttool.vdf'
$pack     = Join-Path $here 'pack-dist.ps1'

# --- 1. Locate SteamCMD ---------------------------------------------------
function Resolve-SteamCmd {
    param([string] $Explicit)
    if ($Explicit) {
        if (Test-Path $Explicit) { return (Resolve-Path $Explicit).Path }
        throw "steamcmd not found at -SteamCmd '$Explicit'."
    }
    $cmd = Get-Command steamcmd, steamcmd.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    foreach ($c in @('C:\steamcmd\steamcmd.exe',
                     "$env:ProgramFiles\steamcmd\steamcmd.exe",
                     "${env:ProgramFiles(x86)}\Steam\steamcmd.exe")) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

$steam = Resolve-SteamCmd -Explicit $SteamCmd
if (-not $steam) {
    Write-Error @"
STEAMCMD NOT FOUND.
Download https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip, extract it to C:\steamcmd,
then re-run (or pass -SteamCmd C:\steamcmd\steamcmd.exe).
"@
    exit 1
}
Write-Host "SteamCMD: $steam" -ForegroundColor Cyan

# --- 2. The item must already exist --------------------------------------
if (-not (Test-Path $vdf)) { throw "VDF not found: $vdf" }
$vdfText = Get-Content -Raw $vdf
if ($vdfText -match 'PUBLISHEDFILEID_PLACEHOLDER') {
    Write-Error @"
NO WORKSHOP ITEM YET: contenttool.vdf still carries PUBLISHEDFILEID_PLACEHOLDER.

Create the item once first (steamugc\publish_ugc.py --create, or the PPWorkshopTool GUI), then put
the real id into contenttool.vdf:

    "publishedfileid" "1234567890"

Never paste an id from another mod: SteamCMD would upload ContentTool over that item.
"@
    exit 1
}

# --- 3. The preview image must exist -------------------------------------
if ($vdfText -notmatch '(?m)^\s*"previewfile"\s*"([^"]+)"') {
    throw "contenttool.vdf has no previewfile line."
}
$preview = $Matches[1]
if (-not (Test-Path $preview)) {
    Write-Error @"
PREVIEW IMAGE MISSING: $preview

Requirements: <= 1 MB, 1024x1024, JPG or PNG. It is produced separately and is not part of this rig.
Nothing is uploaded without it.
"@
    exit 1
}
$previewBytes = (Get-Item $preview).Length
if ($previewBytes -gt 1000000) {
    Write-Error "PREVIEW IMAGE TOO LARGE: $preview is $previewBytes bytes, Steam's limit is 1 MB."
    exit 1
}
Write-Host ("Preview: {0} ({1:N0} B)" -f $preview, $previewBytes) -ForegroundColor Cyan

# --- 4. Build a clean Dist ------------------------------------------------
Write-Host "Packing Dist..." -ForegroundColor Cyan
& $pack
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
    throw "pack-dist.ps1 failed (exit $LASTEXITCODE)."
}

# --- 5. Stamp the change note, then upload -------------------------------
$escaped = $ChangeNote -replace '\\', '\\\\' -replace '"', '\"'
$vdfText = [regex]::Replace($vdfText, '("changenote"\s*")[^"]*(")',
    { param($m) $m.Groups[1].Value + $escaped + $m.Groups[2].Value })
Set-Content -Path $vdf -Value $vdfText -Encoding UTF8 -NoNewline

if (-not $SteamUser) { $SteamUser = Read-Host "Steam account name" }
Write-Host "Uploading to the Steam Workshop as '$SteamUser'..." -ForegroundColor Cyan
Write-Host "(SteamCMD prompts for the password + Steam Guard/2FA in its own console.)"

& $steam +login $SteamUser +workshop_build_item $vdf +quit
$code = $LASTEXITCODE
if ($code -ne 0) {
    Write-Error "SteamCMD exited with code $code. Read its output above."
    exit $code
}
Write-Host "`nDone. Open the item page and check it." -ForegroundColor Green
