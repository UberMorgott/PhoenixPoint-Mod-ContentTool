# The DEFAULT TARGET IS THE TEST INSTALL, never the user's own game. An older ContentTool reached
# D:\Steam\steamapps\common\Phoenix Point through this default and rewrote its aa\catalog.json;
# nothing here may find that install on its own - no fallback, no Steam library scan - so touching
# it takes an explicit -PPRoot and is therefore always a decision somebody made on purpose.
param([string] $PPRoot = "D:\PP-Instance2")
$ErrorActionPreference = 'Stop'
# Loud, first line of every run: a wrong target has to be obvious before the copy, not after it.
Write-Host "==> ContentTool deploy TARGET: $PPRoot" -ForegroundColor Yellow
if (-not (Test-Path $PPRoot)) { throw "no install at $PPRoot - pass -PPRoot <path> to name one." }
# The csproj already builds into a folder named after the assembly, which is exactly the layout
# PPModLoader wants (Mods\<Name>\<Name>.dll + meta.json), so the whole deploy is one copy.
$out  = Join-Path $PSScriptRoot 'bin\Release\ContentTool'
$dest = Join-Path $PPRoot 'Mods\ContentTool'
dotnet build (Join-Path $PSScriptRoot 'ContentTool.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$out\*" $dest -Force

# ============ WHERE THIS COPY CAME FROM, WRITTEN DOWN ============
# The in-game fit workbench SAVES into the deployed ppcontent.json - that is the file the game loaded
# - while the author's truth is the repo, and the next run of this script copies repo OVER deployed.
# So an afternoon of dialling a gun in by eye used to be one forgotten hand-copy away from being
# overwritten by its own older version. Nothing has to be configured for the workbench to mirror a
# save back, because THIS script already knows both paths: it leaves the source folder in a one-line
# marker beside every mod it installs, and src\Dev\BenchList.cs reads it back after a save.
# The name is dot-prefixed and carries the mod's own name, so it can never collide with mod content.
function Write-SourceMarker([string] $Folder, [string] $Source) {
    Set-Content -Path (Join-Path $Folder '.contenttool-source') -Value $Source -Encoding UTF8
}
Write-SourceMarker $dest $PSScriptRoot
Write-Host "Deployed ContentTool to $dest"

# Every demo is its own MOD, not a folder inside ours: PPModLoader discovers only TOP-LEVEL
# directories under Mods\ that hold a meta.json (decompiled PPModLoader.cs:29-46), so a demo living
# under Mods\ContentTool\ can never be listed or switched off. One folder per demo beside us.
# Only the shipped parts are copied - sources, build output and authoring tools are not a mod.
$mods = Split-Path $dest -Parent
# local\ is the same loop over content that must never ship (gitignored, see local\README.md), so a
# scratch model can be looked at in game without ever being a candidate for the Workshop upload.
$folders = @(Get-ChildItem (Join-Path $PSScriptRoot 'demos') -Directory)
if (Test-Path (Join-Path $PSScriptRoot 'local')) {
    $folders += Get-ChildItem (Join-Path $PSScriptRoot 'local') -Directory
}
foreach ($demo in $folders) {
    if (-not (Test-Path (Join-Path $demo.FullName 'meta.json'))) { continue }
    $to = Join-Path $mods $demo.Name
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    # Icons\ ships too: the weapon demos read <mod>\Icons\*.png at OnModEnabled to write the
    # inventory cell's sprite, and leaving it out is what made both print "no icon at ..." in-game
    # while the file sat in the repo all along.
    # THE DATA-LOSS WINDOW, out loud. If the deployed manifest differs from the one about to land on
    # top of it, the author tuned a fit in game and has not mirrored it back (or the mirror failed).
    # A WARNING, not a block: overwriting is usually exactly what a deploy is for, and a prompt here
    # would sit unanswered in every scripted run.
    $liveManifest = Join-Path $to 'ppcontent.json'
    $repoManifest = Join-Path $demo.FullName 'ppcontent.json'
    if ((Test-Path $liveManifest) -and (Test-Path $repoManifest) -and
        (Get-FileHash $liveManifest).Hash -ne (Get-FileHash $repoManifest).Hash) {
        Write-Host ("WARNING: $($demo.Name)\ppcontent.json in the game DIFFERS from the repo copy " +
                    "about to overwrite it - if you tuned this weapon in the workbench, that tuning " +
                    "is in $liveManifest and is being lost now.") -ForegroundColor Red
    }
    foreach ($item in 'meta.json', 'ppcontent.json', 'README.md', 'SOURCES.md', 'Content', 'Icons', 'Dist') {
        $from = Join-Path $demo.FullName $item
        if (Test-Path $from) { Copy-Item $from $to -Recurse -Force }
    }
    Write-SourceMarker $to $demo.FullName
    # The two demos that ship a trigger build their own DLL; the other four are content only.
    $csproj = Get-ChildItem $demo.FullName -Filter '*.csproj' | Select-Object -First 1
    if ($csproj) {
        dotnet build $csproj.FullName -c Release
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $($demo.Name) (exit $LASTEXITCODE)." }
        $dll = Get-ChildItem (Join-Path $demo.FullName 'bin\Release') -Recurse -Filter "$($demo.Name).dll" |
               Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $dll) { throw "no $($demo.Name).dll under $($demo.FullName)\bin\Release." }
        Copy-Item $dll.FullName $to -Force
    }
    Write-Host "Deployed demo $($demo.Name) to $to"
}
