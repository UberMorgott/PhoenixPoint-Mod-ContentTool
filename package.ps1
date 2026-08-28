# Build a PUBLISHABLE mod folder out of an author folder: zip what this writes, upload that.
#
# A SCRIPT AND NOT A ct_ VERB. A verb would mean launching Phoenix Point, opening the developer
# console and digging the result out of Player.log every time a modder cuts a release. This runs
# with the game shut, which is when releases are actually cut. The rule it enforces lives in
# src\Project\Package.cs and is compiled into the mod too, so nothing here is a second implementation.
#
#   .\package.ps1 -Project demos\WeaponAdd
#   .\package.ps1 -Project D:\MyMod -Out D:\MyMod-release
#
# It does NOT bake. The mod's own bundle and its banks need Unity's decoders and the player's own
# installation, so they are produced in game by 'ct_project <YourMod>' / 'ct_sound bake <YourMod>'
# and land in the project's Dist\ - which is what this copies. A project with nothing baked is
# refused by name rather than packaged empty.
param(
    [Parameter(Mandatory = $true)] [string] $Project,
    [string] $Out
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not (Test-Path $Project)) { throw "no author folder at $Project" }
$Project = (Resolve-Path $Project).Path
$name = Split-Path $Project -Leaf
if (-not $Out) { $Out = Join-Path $root "dist-package\$name" }
Write-Host "==> packaging $Project -> $Out" -ForegroundColor Yellow

# A previous run's folder is REMOVED here, never merged into: the packager refuses a non-empty
# target on purpose, so that a leftover from an older revision can never ride along into a release.
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }

# The mod's own DLL, when it has one. Same build the deploy does, so a release cannot ship an
# assembly older than the one that was last tested in game.
$assembly = $null
$csproj = Get-ChildItem $Project -Filter '*.csproj' | Select-Object -First 1
if ($csproj) {
    dotnet build $csproj.FullName -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $name (exit $LASTEXITCODE)." }
    $dll = Get-ChildItem (Join-Path $Project 'bin\Release') -Recurse -Filter "$name.dll" |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $dll) { throw "no $name.dll under $Project\bin\Release." }
    $assembly = $dll.FullName
}

# $argv, not $args: $args is an automatic variable in PowerShell and assigning to it is a trap.
$argv = @($Project, $Out)
if ($assembly) { $argv += $assembly }
dotnet run --project (Join-Path $root 'tools\Package\Package.csproj') -c Release -- @argv
if ($LASTEXITCODE -ne 0) { throw "package REFUSED (exit $LASTEXITCODE) - nothing was written." }
