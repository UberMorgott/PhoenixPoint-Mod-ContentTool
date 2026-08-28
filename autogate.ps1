<#
  Runs the in-game gates without a human typing them.

  The gates still need a live Unity - texture, mesh, Wwise and Addressables behaviour cannot be
  faked, and a gate that proves a simulator proves nothing. What this removes is the person.

    .\autogate.ps1                                   # the default gate list, one launch
    .\autogate.ps1 -Commands ct_bake,ct_audio        # a specific list
    .\autogate.ps1 -Commands ct_project -Then 'ct_route7 verify'   # two launches
    .\autogate.ps1 -KeepOpen                         # leave the last launch running

  There is no -UnityAudio switch any more, and nothing here edits a game file. It used to flip
  Unity's m_DisableAudio in globalgamemanagers so the ENGINE could decode an author's .ogg/.mp3;
  the tool decodes both itself now (WwisePcm.ReadAudio, NVorbis + NLayer), so `ct_project` bakes a
  project holding compressed audio in an ordinary launch like any other.

  -Then is the two-phase shape: the catalog is parsed once at startup, so anything written in
  launch 1 is only verifiable in launch 2. Each launch reads autorun.txt fresh, so the split lives
  here and the mod needs to know nothing about it.
#>
param(
    [string[]] $Commands = @('ct_bake', 'ct_audio', 'ct_project', 'ct_texswap', 'ct_meshswap', 'ct_scan gate', 'ct_liveswap'),
    [string[]] $Then     = @(),
    # The TEST install, never the user's own game - the same default deploy.ps1 was moved to in
    # cce6302. This script launches and kills a process, so the wrong default here is worse than
    # there: reaching the author's own install took nothing but forgetting a flag.
    [string]   $PPRoot   = 'D:\PP-Instance2',
    [int]      $TimeoutSeconds = 300,
    [int]      $InitTimeoutSeconds = 90,
    [switch]   $KeepOpen,
    [switch]   $NoDeploy
)

$ErrorActionPreference = 'Stop'
$modDir  = Join-Path $PPRoot 'Mods\ContentTool'
$autorun = Join-Path $modDir 'autorun.txt'
$exe     = Join-Path $PPRoot 'PhoenixPointWin64.exe'

if (-not $NoDeploy) { & (Join-Path $PSScriptRoot 'deploy.ps1') | Select-Object -Last 1 }
if (-not (Test-Path $modDir)) { throw "ContentTool is not deployed: $modDir missing. Run deploy.ps1 first." }
if (-not (Test-Path $exe))    { throw "No game executable at $exe" }

# A Phoenix Point that this script did not start is SOMEONE ELSE'S - the author testing another mod,
# or a parallel session mid-run. This gate has exactly one launch it may ever kill: the one
# Start-Process hands back below, by its own PID. It must never kill by NAME, and it must not quietly
# launch into a copy that is already occupied either, because the game is single-instance PER INSTALL
# and the second launch dies on the spot with an empty log that reads like a mod failure (measured
# 2026-08-12: "no log was written", then 90s of "never initialised" on the retry).
#
# The comparison is by EXECUTABLE PATH, not by process name: D:\PP-Instance2 exists precisely so a
# second copy can run beside the author's, so a game running out of a different root is none of this
# run's business and must not block it.
$mine = (Get-Item $exe).FullName
$busy = @(Get-CimInstance Win32_Process -Filter "Name='PhoenixPointWin64.exe'" |
         Where-Object { $_.ExecutablePath -and (Get-Item $_.ExecutablePath).FullName -eq $mine })
if ($busy.Count -gt 0) {
    throw ("REFUSED: Phoenix Point is already running from $PPRoot (PID " +
           ($busy.ProcessId -join ', ') + "), started by someone other than this script. " +
           'It is NOT killed - this gate only ever kills the PID it launched itself. ' +
           'Either wait for it, or run the gate on the provisioned second copy: ' +
           'deploy into it (D:\PP-Instance2\launch-instance.bat sync) and ' +
           '.\autogate.ps1 -PPRoot D:\PP-Instance2 -NoDeploy')
}

# Which build a session RAN is not the same question as which build is on disk: a game launched
# before the deploy keeps the old assembly and every gate line it prints is a ghost. The mod stamps
# the SHA-1 of the DLL it loaded into its init line; this is the same hash, computed here.
$expected = (Get-FileHash -Algorithm SHA1 (Join-Path $modDir 'ContentTool.dll')).Hash.ToLower().Substring(0, 8)
Write-Host "expecting build=$expected"

function Invoke-Phase {
    param([string[]] $List, [int] $Number, [bool] $Last)

    # The log path carries the INSTALL and this script's own PID. It used to be
    # ct-autogate-phase<N>.log for every agent on the machine, so two runs raced: one overwrote the
    # other's evidence between the run finishing and the read, and an empty file reads exactly like
    # "the gate printed nothing". Measured 2026-08-12 - a voice-count run came back blank because a
    # parallel agent's launch had truncated the file.
    $phaseLog = Join-Path $env:TEMP ("ct-autogate-" + ($PPRoot -replace '[^A-Za-z0-9]', '') + "-$PID-phase$Number.log")
    if (Test-Path $phaseLog) { Write-Warning "phase ${Number}: $phaseLog already exists and is being replaced"; Remove-Item $phaseLog -Force }
    Set-Content -Path $autorun -Value $List -Encoding UTF8
    Write-Host "phase ${Number}: $($List -join ', ')"

    # -mods is what turns PPModLoader on (Base.Core\Game.cs:252-262 strips the dash,
    # PhoenixPoint.Common.Game\PhoenixGame.cs:172-177 maps "mods" -> ModManager.CanUseMods).
    # It is a PROCESS argument, so the exe is launched directly rather than through Steam: a
    # steam:// URL cannot carry a per-run flag (it would have to live in Steam's launch options,
    # which is persistent global state) and returns no process to wait on or kill. -logFile keeps
    # this run's Unity log out of the shared LocalLow one, so nothing has to be deleted or copied.
    # -PassThru is load-bearing, not convenience: $game.Id is the ONLY handle either Stop-Process
    # below is allowed to use. Never Get-Process/Stop-Process by name here - that would reach into
    # the author's own game, which is exactly how one got killed on 2026-08-12.
    $game = Start-Process -FilePath $exe -ArgumentList '-mods', '-logFile', $phaseLog -PassThru
    Write-Host "phase ${Number}: launched PID $($game.Id) (the only process this run may stop)"

    $start = Get-Date
    $inited = $false; $found = $false; $stamp = '(no init line)'
    while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Seconds 3
        if ($game.HasExited) { Write-Warning "phase ${Number}: the game exited before the marker"; break }
        if (-not (Test-Path $phaseLog)) { continue }

        if (-not $inited) {
            $line = Select-String -Path $phaseLog -Pattern 'ContentTool \d.*build=([0-9a-f]{8})' | Select-Object -First 1
            if ($line) { $inited = $true; $stamp = $line.Matches[0].Groups[1].Value }
            elseif (((Get-Date) - $start).TotalSeconds -gt $InitTimeoutSeconds) {
                Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
                Remove-Item $autorun -Force -ErrorAction SilentlyContinue
                throw "phase ${Number}: the game launched but ContentTool never initialised within ${InitTimeoutSeconds}s - is it launching with -mods? (log: $phaseLog)"
            }
        }
        if (Select-String -Path $phaseLog -Pattern 'ct_autorun: DONE' -Quiet) { $found = $true; break }
    }

    if (-not ($KeepOpen -and $Last) -and -not $game.HasExited) { Stop-Process -Id $game.Id -Force; Start-Sleep -Seconds 3 }
    if (-not (Test-Path $phaseLog)) { throw "phase ${Number}: no log was written to $phaseLog" }
    if (-not $found) { Write-Warning "phase ${Number}: marker 'ct_autorun: DONE' never appeared" }

    if ($stamp -ne $expected) {
        Write-Warning "phase ${Number}: THE SESSION RAN build=$stamp, NOT the deployed $expected - every gate line below is a ghost"
    } else { Write-Host "phase ${Number}: confirmed build=$stamp" }
    return @{ Log = $phaseLog; Found = $found; Build = ($stamp -eq $expected) }
}

$phases = @(, $Commands)
if ($Then.Count -gt 0) { $phases += , $Then }

$results = @()
try {
    for ($i = 0; $i -lt $phases.Count; $i++) {
        $results += Invoke-Phase -List $phases[$i] -Number ($i + 1) -Last ($i -eq $phases.Count - 1)
    }
}
finally {
    Remove-Item $autorun -Force -ErrorAction SilentlyContinue   # never fire on a later manual launch
}

$ok = $true
for ($i = 0; $i -lt $results.Count; $i++) {
    Write-Host ''
    Write-Host "--- phase $($i + 1) ($($results[$i].Log)) ---"
    # VOID is in the pattern on purpose: a gate that could not answer must be visible here, or a run
    # that measured nothing reads exactly like a run that measured everything.
    # Strip the Unity log prefix ONLY - '[INFO] 34 (1,847): '. The old rule cut everything up to the
    # last '| ', which silently ate the GATE NAME off every arm whose summary contains a pipe
    # (U3a-refs, U4-wrote, U5-wrote): a FAIL there would have been printed as an anonymous fragment.
    Select-String -Path $results[$i].Log -Pattern 'ct_autorun|PASS|FAIL|VOID|REFUSED|THREW|FAILURE' |
        ForEach-Object { $_.Line -replace '^\[\w+\] \d+ \([^)]*\): ', '' }
    if (-not $results[$i].Found -or -not $results[$i].Build) { $ok = $false }
}
if (-not $ok) { exit 1 }
