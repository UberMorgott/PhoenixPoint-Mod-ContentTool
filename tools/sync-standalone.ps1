# Mirror the monorepo's COMMITTED ContentTool tree into the standalone publishing repo.
#
#   .\tools\sync-standalone.ps1
#
# The rule for docs: if a page is not published on the site, it does not belong in the public
# repo. The list below is `mkdocs.yml`'s `exclude_docs`, kept in step with it by hand - there is
# no second source of truth, only a second copy of the same one.
#
# The script does not review the standalone's own deletions: a file dropped from the monorepo
# still has to be `git rm`'d there by hand. Extraction only overwrites.

param(
    [string] $Mono       = 'E:\DEV\PhoenixPoint',
    [string] $Standalone = 'E:\DEV\PhoenixPoint\ContentTool-standalone'
)

$ErrorActionPreference = 'Stop'

$internal = @(
    'docs\README.md'
    'docs\FINAL-PLAN.md'
    'docs\METHODOLOGY.md'
    'docs\RECIPES.md'
    'docs\PROVEN-FOUNDATIONS.md'
    'docs\RELEASE.md'
    'docs\VERIFIED-DEMOS.md'
    'docs\animated-model-cases.md'
    'docs\HANDOFF-*.md'
    'docs\research-*.md'
    'docs\design-*.md'
    'docs\blind-test'
)

# A tar file, not a pipe: PowerShell mangles binary on the way through one.
$tar = Join-Path $env:TEMP 'contenttool-sync.tar'
git -C $Mono archive -o $tar HEAD ContentTool/
if ($LASTEXITCODE -ne 0) { throw "git archive failed" }
tar -x -f $tar -C $Standalone --strip-components=1
if ($LASTEXITCODE -ne 0) { throw "tar extract failed" }
Remove-Item $tar

foreach ($pattern in $internal) {
    Get-Item (Join-Path $Standalone $pattern) -ErrorAction Ignore | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
        Write-Host "excluded: $($_.FullName.Substring($Standalone.Length + 1))"
    }
}

git -C $Standalone status --short
