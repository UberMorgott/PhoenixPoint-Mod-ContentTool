# Build the SEALED workspace for the source-blind documentation test.
#
# The blind agent must be unable to read the source even by accident, so isolation is PHYSICAL:
# it gets a directory that contains the rendered documentation and nothing else. A brief that
# merely FORBIDS reading src\ is not isolation - the agent starts inside a tree that holds the
# source, the demos, the git history and every earlier agent's log.
#
#   .\seal-blind-workspace.ps1 -Round 1
#
# Produces:
#   <sealed>\docs\      the rendered site (HTML), which is exactly what a stranger on GitHub sees
#   <sealed>\work\      empty, the agent's scratch space
#   <sealed>\MANIFEST.txt   what was let in, hashed, so a later round can prove what changed
param(
    [Parameter(Mandatory = $true)] [int] $Round,
    [string] $Repo,
    [string] $Root,
    [string] $Python
)
$ErrorActionPreference = 'Stop'
# Defaults are DERIVED, never hardcoded: a path that exists only on the author's machine makes the
# script work for exactly one person, which is the failure this repository has already paid for once.
if (-not $Repo)   { $Repo   = Split-Path $PSScriptRoot -Parent }
if (-not $Root)   { $Root   = Join-Path ([IO.Path]::GetTempPath()) 'contenttool-blind' }
if (-not $Python) {
    $venv = Join-Path $Repo '.venv\Scripts\python.exe'
    $Python = if (Test-Path $venv) { $venv } else { 'python' }
}
$sealed = Join-Path $Root "round$Round"
Write-Host "==> sealing round $Round into $sealed" -ForegroundColor Yellow

# Rebuild the site first: the agent must be tested against the CURRENT docs, and --strict means a
# broken link fails here rather than becoming a "documentation defect" the agent gets blamed for.
Push-Location $Repo
try {
    & $Python -m mkdocs build --strict
    if ($LASTEXITCODE -ne 0) { throw "mkdocs build --strict failed - fix the docs before sealing." }
} finally { Pop-Location }

# A previous round is REMOVED, never merged into: a leftover page from an earlier round would let
# the agent read documentation that no longer exists, and the round's verdict would be void.
#
# But work\ is the agent's DELIVERABLE, not documentation - the mod it built, its command list and
# its findings. Re-sealing a round to verify a fix used to delete all three (measured: round 2 lost
# COMMANDS.md and FINDINGS.md that way), which destroys the only evidence the round produced. So it
# is moved aside first, never deleted, and the path is printed.
if (Test-Path $sealed) {
    $work = Join-Path $sealed 'work'
    if ((Test-Path $work) -and @(Get-ChildItem $work -Force).Count -gt 0) {
        $keep = "$sealed-work-" + (Get-Date -Format 'yyyyMMdd-HHmmss')
        Move-Item $work $keep
        Write-Host "kept the previous round's work\ at $keep" -ForegroundColor Yellow
    }
    Remove-Item $sealed -Recurse -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $sealed 'work') | Out-Null
Copy-Item (Join-Path $Repo 'site') (Join-Path $sealed 'docs') -Recurse -Force

# The leak check. `mkdocs build` already drops the internal pages via exclude_docs, but a link or a
# code sample inside a PUBLISHED page can still name a repository path and hand the agent the answer.
# Anything matched here is a documentation defect in its own right, so it fails the seal.
$leaks = Get-ChildItem (Join-Path $sealed 'docs') -Recurse -File -Include *.html, *.md |
    Select-String -List `
        -Pattern '(^|[^A-Za-z0-9_])src[\\/](Project|Bake|Tactical|Import|Live)[\\/]|ContentTool[\\/]src[\\/]|tests[\\/]TargetPathTests|FINAL-PLAN|HANDOFF-|E:\\DEV\\PhoenixPoint'
if ($leaks) {
    $leaks | ForEach-Object { Write-Host "LEAK $($_.Path): $($_.Line.Trim())" -ForegroundColor Red }
    throw "the rendered site names the repository or an internal document - fix the docs, then re-seal."
}

Get-ChildItem (Join-Path $sealed 'docs') -Recurse -File |
    Get-FileHash -Algorithm SHA1 |
    ForEach-Object { "{0}  {1}" -f $_.Hash, $_.Path.Substring($sealed.Length + 1) } |
    Set-Content (Join-Path $sealed 'MANIFEST.txt') -Encoding UTF8

$n = (Get-ChildItem (Join-Path $sealed 'docs') -Recurse -File).Count
Write-Host "sealed $n file(s), no leaks. Agent may read ONLY $sealed" -ForegroundColor Green
