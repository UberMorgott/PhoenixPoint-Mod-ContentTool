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
Write-Host "Deployed ContentTool to $dest"

# Every demo is its own MOD, not a folder inside ours: PPModLoader discovers only TOP-LEVEL
# directories under Mods\ that hold a meta.json (decompiled PPModLoader.cs:29-46), so a demo living
# under Mods\ContentTool\ can never be listed or switched off. One folder per demo beside us.
# Only the shipped parts are copied - sources, build output and authoring tools are not a mod.
$mods = Split-Path $dest -Parent
foreach ($demo in Get-ChildItem (Join-Path $PSScriptRoot 'demos') -Directory) {
    if (-not (Test-Path (Join-Path $demo.FullName 'meta.json'))) { continue }
    $to = Join-Path $mods $demo.Name
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    # Icons\ ships too: the weapon demos read <mod>\Icons\*.png at OnModEnabled to write the
    # inventory cell's sprite, and leaving it out is what made both print "no icon at ..." in-game
    # while the file sat in the repo all along.
    foreach ($item in 'meta.json', 'ppcontent.json', 'README.md', 'SOURCES.md', 'Content', 'Icons', 'Dist') {
        $from = Join-Path $demo.FullName $item
        if (Test-Path $from) { Copy-Item $from $to -Recurse -Force }
    }
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
