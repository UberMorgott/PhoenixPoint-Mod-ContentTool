> **Superseded historical record.** This is the 2026-08-24/25 session note that PPCLI was designed
> from: the `autogate.ps1` + `autorun.txt` + log-grep loop, before the live named pipe replaced it.
> Kept for the environment facts and the traps, not as a current runbook - `autogate.ps1`'s
> `-PPRoot` default has since changed, and PPCLI (`PPCLI\README.md`) is the tool to reach for now.
> The author's own install path has been replaced by `<game install root>` throughout.
# Driving Phoenix Point headlessly — the harness, as actually operated

Written from a real session (2026-08-24/25) that used this harness to fix two in-game defects in a
custom-creature mod. Everything below was either **run by me** or **read out of the source that ran**.
Provenance is marked per section, because a CLI built on a guess fails silently here.

- `[RAN]` — I executed this and read the result.
- `[SRC]` — I read the implementation; it executed during my run but I did not drive it directly.
- `[PRIOR]` — recorded by earlier sessions in `ContentTool\docs\`; I did **not** re-verify it.

Source of truth if this doc and the code disagree: `ContentTool\autogate.ps1`,
`ContentTool\src\Dev\AutoRun.cs`, `ContentTool\src\Tactical\CreatureGate.cs`.

---

## 1. The shape of the thing

There is no headless mode and no scripting API. What exists is a loop:

```
deploy DLL  ->  write autorun.txt  ->  launch PhoenixPointWin64.exe -mods -logFile <tmp>
            ->  mod reads autorun.txt at scene-ready, runs each line as a console command
            ->  results are printed to the Unity log  ->  poll for "ct_autorun: DONE"
            ->  kill the PID we launched  ->  grep the log
```

The game is a real Unity process with a real scene. That is not a defect of the harness — texture,
mesh, Wwise, Addressables and animator behaviour cannot be faked, and a gate that proves a simulator
proves nothing. What the harness removes is the **human**, not the engine.

Round-trip cost, measured `[RAN]`: ~40 s for a bake-only phase, **~6-9 min** for a phase that loads a
tactical savegame and runs a mission gate. Budget accordingly; do not poll a CLI synchronously.

---

## 2. Environment facts a new tool needs

| Fact | Value |
|---|---|
| Install root (default) | `<game install root>` |
| Executable | `<root>\PhoenixPointWin64.exe` |
| Mods dir | `<root>\Mods\<ModName>\` |
| Deployed engine DLL | `<root>\Mods\ContentTool\ContentTool.dll` |
| Command file | `<root>\Mods\ContentTool\autorun.txt` |
| Per-phase Unity log | `%TEMP%\ct-autogate-<sanitised root>-<script PID>-phase<N>.log` |
| Second provisioned copy | `D:\PP-Instance2` |
| Managed assemblies | `<root>\PhoenixPointWin64_Data\Managed\` |
| Mod SDK assembly | `<root>\ModSDK\Assembly-CSharp.dll` |

`[RAN]` The launch flag is `-mods`. `[SRC]` It is what turns PPModLoader on — `Base.Core\Game.cs:252-262`
strips the dash, `PhoenixPoint.Common.Game\PhoenixGame.cs:172-177` maps `"mods"` -> `ModManager.CanUseMods`.
It is a **process argument**, so launch the exe directly, never through a `steam://` URL: a Steam URL
cannot carry a per-run flag (it would have to live in Steam's persistent global launch options) and
returns no process handle to wait on or kill.

`-logFile <path>` `[RAN]` redirects this run's Unity log away from the shared LocalLow one, so nothing
has to be deleted, copied, or de-interleaved from a parallel run.

### Profile state — restore byte-exact

`[PRIOR]` Enabled mods are recorded in `Options.jopt`, which is **per SteamID** — the author's main
install and `D:\PP-Instance2` have different ones (recorded as `…591` and `…592`). Enabling a mod adds
its id (e.g. `com.morgott.ContentTool`) to `MOD_ACTIVATED` **and it stays** across runs.

Two recorded traps, neither re-verified by me:
- `[PRIOR]` A mod that **fails to load** makes Phoenix Point rewrite `MOD_ACTIVATED` **empty**, silently
  disabling *every other mod too* (measured 2026-08-13; see `ContentTool\src\Bake\CatalogLive.cs:48`,
  `docs\SHIPPING-A-CONTENT-MOD.md:81`). So one broken mod looks like "the harness stopped working".
  A CLI must snapshot `Options.jopt` before a run and diff it after.
- `[PRIOR]` Editing `Options.jopt` by re-serialising the JSON shrank it **32991 -> 18996 B**. It still
  parsed, but do not round-trip it — patch bytes or leave it alone
  (`docs\HANDOFF-sound-redesign.md:130`).

`[PRIOR]` Generated/patched assets belong in `Application.persistentDataPath\ContentTool\Patched\<modid>\`,
never inside the mod folder. I did **not** resolve the absolute `persistentDataPath` this session —
a CLI should read it from the game rather than hardcode a LocalLow path.

**Rule I operated under `[RAN]`:** leave the install exactly as found — mod flags unchanged,
`Options.jopt` byte-exact, no `autorun.txt` left behind, no PP process left running. I verified all
four at the end of the session.

---

## 3. The launch harness — `ContentTool\autogate.ps1`

### Parameters (verbatim from the `param()` block)

| Param | Default | What it does |
|---|---|---|
| `-Commands` | `@('ct_bake','ct_audio','ct_project','ct_texswap','ct_meshswap','ct_scan gate','ct_liveswap')` | phase-1 command list, one console line each |
| `-Then` | `@()` | phase-2 command list; non-empty adds a **second launch** |
| `-PPRoot` | `<game install root>` | which install to drive |
| `-TimeoutSeconds` | `300` | per-phase wall clock before giving up on the DONE marker |
| `-InitTimeoutSeconds` | `90` | how long to wait for the mod's own init line before declaring "not launching with `-mods`" |
| `-KeepOpen` | switch | leave the **last** launch running instead of killing it |
| `-NoDeploy` | switch | skip `deploy.ps1` (use when the install is already current, or when driving `D:\PP-Instance2`) |

### Invocations I actually used `[RAN]`

```powershell
# two-phase: bake the demo's bundle, then measure it in a fresh process
.\autogate.ps1 -Commands 'ct_project CustomCreature' -Then 'ct_creature gate 4 ct_creature_morgott'

# single phase, re-running only the gate after a DLL change
.\autogate.ps1 -Commands 'ct_creature gate 4 ct_creature_morgott'
```

Both were launched with `run_in_background` and their console output tee'd to a file. A phase that
loads a mission exceeds a 10-minute foreground tool timeout; do not block on it.

### The `-Commands` / `-Then` two-phase mechanic `[SRC]`

`$phases = @(, $Commands); if ($Then.Count -gt 0) { $phases += , $Then }` — each phase is a **separate
process launch** that rewrites `autorun.txt` from scratch.

Why it exists: **the Addressables catalog is parsed once at startup.** Anything a bake *writes* in
launch 1 is only *visible* in launch 2. The split lives in the script, not in the mod — the mod just
reads `autorun.txt` fresh each launch and knows nothing about phases.

Practical consequence for a CLI: **any command that changes on-disk assets must be its own phase**, and
the command that verifies it must be in a later phase.

### The single-instance collision guard `[SRC]` — the part that matters most

Phoenix Point is single-instance **per install**. A second launch into an occupied install **dies on
the spot with an empty log**, which reads exactly like "the mod crashed" (recorded: `"no log was
written"`, then 90 s of `"never initialised"` on the retry).

The script refuses instead of racing:

```powershell
$mine = (Get-Item $exe).FullName
$busy = @(Get-CimInstance Win32_Process -Filter "Name='PhoenixPointWin64.exe'" |
         Where-Object { $_.ExecutablePath -and (Get-Item $_.ExecutablePath).FullName -eq $mine })
if ($busy.Count -gt 0) { throw "REFUSED: Phoenix Point is already running from $PPRoot (PID ...)" }
```

Three rules encoded there, all of which a CLI must copy:
1. **Compare by executable PATH, not process name.** `D:\PP-Instance2` exists precisely so a second
   copy can run alongside; a game running from a different root is none of this run's business.
2. **Never kill by name.** The only PID this run may stop is the one `Start-Process -PassThru` handed
   back. `-PassThru` is load-bearing, not convenience. A name-based kill once killed the author's own
   game (2026-08-12).
3. **Refuse, don't wait.** The suggested fallback is the second copy:
   `.\autogate.ps1 -PPRoot D:\PP-Instance2 -NoDeploy`.

`[RAN]` I hit this at session start: a `PhoenixPointWin64.exe` (PID 14076, started 23:56, `-mods`,
from the token install) was the user's own session. I refused to deploy or launch until it exited on
its own. I never killed it.

### The build-stamp guard `[SRC]` — the false-green killer

"Which build is on disk" and "which build the session RAN" are different questions. A game launched
before a deploy keeps the old assembly and **every line it prints is a ghost**.

```powershell
$expected = (Get-FileHash -Algorithm SHA1 (Join-Path $modDir 'ContentTool.dll')).Hash.ToLower().Substring(0,8)
# ...mod stamps the SHA-1 of the DLL it loaded into its init line:
Select-String -Path $phaseLog -Pattern 'ContentTool \d.*build=([0-9a-f]{8})'
```

Mismatch prints `THE SESSION RAN build=<x>, NOT the deployed <y> - every gate line below is a ghost`
and the script exits 1. `[RAN]` My runs printed `expecting build=0e555822` / `phase 1: confirmed
build=0e555822` and later `expecting build=3be3491b`. **A CLI without this check will report stale
results as passes.**

### Exit semantics `[SRC]`

`exit 1` if, for any phase, the DONE marker never appeared **or** the build stamp mismatched. The
`finally` block always removes `autorun.txt` so it can never fire on a later manual launch.

---

## 4. The command channel — `autorun.txt` + `src\Dev\AutoRun.cs`

### Writing it `[SRC]`

```powershell
$autorun = Join-Path $modDir 'autorun.txt'      # <root>\Mods\ContentTool\autorun.txt
Set-Content -Path $autorun -Value $List -Encoding UTF8
```

One console command per line. `[SRC]` `AutoRun.Begin` trims each line and **skips blanks and lines
starting with `#`**, so the file is commentable.

### Picking it up `[SRC]` (`AutoRun.MaybeStart`, `AutoRun.cs:42-51`)

```csharp
string path = Environment.GetEnvironmentVariable("CT_AUTORUN");
if (string.IsNullOrEmpty(path)) path = Path.Combine(modDir ?? ".", "autorun.txt");
if (!File.Exists(path)) return;       // the normal case: nothing exists, nothing runs
```

- `CT_AUTORUN` env var overrides the path. `[not verified by me]` — I only used the default file.
- Dev-only by construction: a released mod ships no `autorun.txt` and nothing in the bake writes one,
  so with the file absent the class never allocates.
- Runs on a `DontDestroyOnLoad` `MonoBehaviour` because waiting for the scene needs a tick.

### When it fires `[SRC]`

Not a blind timer — the precondition is **the scene has a rigged renderer**:

```csharp
private static bool SceneHasRenderers() {
    foreach (SkinnedMeshRenderer r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
        if (r.gameObject.scene.IsValid()) return true;
    return false;
}
```

Checked every `SettleFrames = 30` frames (~half a second). If `TimeoutSeconds = 120f` elapses first it
runs anyway and logs `scene never showed a rigged renderer within 120s - running anyway, gates that
need one will refuse by name`.

### Isolation between commands `[SRC]`

```csharp
try { log("ct_autorun > " + line); log(ContentToolMain.RunConsoleLine(line)); }
catch (Exception ex) { log("ct_autorun: '" + line + "' THREW " + ex.Message); }
```

One failing command does not take the rest of the list with it. Every command echoes as
`ct_autorun > <line>` before its output — that is your per-command delimiter when parsing the log.

### The DONE marker and the async problem `[SRC]` — subtle and important

`Marker = "ct_autorun: DONE"`, printed as `ct_autorun: DONE <n> command(s)`.

A console command can **return before its work finishes**. `ct_creature gate` returns a one-line
"armed" acknowledgement immediately and the real measurement runs in a coroutine for minutes. If DONE
printed at that moment, autogate would kill the game mid-measurement and read *a run that measured
nothing* as *a run that measured everything*.

The fix is a counter, `Dev.AsyncGate.Pending`:
- an async command does `Dev.AsyncGate.Pending++` when it arms and `--` in its `finally`
- `AutoRun` will not print DONE while `Pending > 0`
- the async phase gets its own budget: `TimeoutSeconds = 120f` for the synchronous list,
  **`AsyncTimeoutSeconds = 600f`** after it
- on async timeout it prints `ct_autorun: <n> async gate(s) still running after 600s - their arms did
  NOT print, treat this run as VOID`

**A CLI adding its own long-running command MUST participate in this counter**, or its results will be
truncated by the harness killing the process.

### Reading results back `[RAN]`

Everything is in the `-logFile`. Unity prefixes lines like `[INFO] 34 (1,847): `. autogate strips
**only** that prefix:

```powershell
Select-String -Path $log -Pattern 'ct_autorun|PASS|FAIL|VOID|REFUSED|THREW|FAILURE' |
    ForEach-Object { $_.Line -replace '^\[\w+\] \d+ \([^)]*\): ', '' }
```

`VOID` is in the pattern deliberately: a gate that *could not answer* must be visible, or a run that
measured nothing reads exactly like a run that measured everything.

`[RAN]` These logs are big — one phase log was **277 KB**, and single lines can be tens of KB (a mesh
arm printed all 5461 vertex indices). **Never read one whole.** Extract with a narrow pattern and
truncate every line:

```powershell
Select-String -Path $p -Pattern '^C1-|ct_creature: C1' |
  ForEach-Object { $_.Line.Substring(0,[Math]::Min(220,$_.Line.Length)) }
```

---

## 5. Naming the target

`[RAN]` Gate usage is `ct_creature gate <tactical savename> [template name fragment]`, e.g.
`ct_creature gate 4 ct_creature_morgott` — save `4`, fragment `ct_creature_morgott`.

`[SRC]` Argument split (`CreatureGate.cs:56-68`): **the fragment is the LAST token**, because a save
name may contain spaces and a def name may not.

```csharp
string who  = args.Length > 2 ? args[args.Length - 1] : null;
string save = string.Join(" ", args.Skip(1).Take(args.Length - (who == null ? 1 : 2)).ToArray());
```

**Why the fragment is not optional in practice `[SRC]`:** with no fragment the gate takes
`Candidates().FirstOrDefault()`, and `Candidates()` is *every* installed `TacCharacterDef` with a rig
and no skinned bodypart items, ordered by name (`CreatureGate.cs:102-111`). That set includes shipped
units — it will happily grab a vanilla Fireworm and report failures that have nothing to do with your
content. Matching is `name.IndexOf(wanted, OrdinalIgnoreCase) >= 0`. **Always pass the fragment.**

`[SRC]` Savegame resolution (`Load`, `CreatureGate.cs:563-583`) is by exact name, and refuses rather
than guesses:
- `hits.Count != 1` -> `REFUSED: <n> savegames answer to '<name>' (run 'ct_mission list')`
- not `IsTacticalSave` or not `IsLoadable()` -> `REFUSED: '<name>' does not declare itself a loadable
  tactical save`

Then `GameUtl.GameComponent<PhoenixGame>().FinishLevelAndLoadGame(pp)`, driven by
`GameUtl.GameComponent<TimeSource>().Timing.Start(Load(saveName))`.

---

## 6. Spawning and positioning `[SRC]` (`CreatureGate.Spawn`, `CreatureGate.cs:485-517`)

This is the part a new tool most needs verbatim. The gate does **not** use `SpawnActorAbility`; it
reproduces `SpawnActorAbility.GetActorInstanceData:47-110` + `:131` without the ability.

```csharp
TacCharacterData data = ((TacCharacterData)def.InstanceData).Clone() as TacCharacterData;
Shipped = data.Strength;
if (data.Strength <= 0) data.Strength = 20;              // see "born dead" below
ComponentSetDef      set  = data.GenerateInstanceComponentSetDef();
TacActorInstanceData inst = (TacActorInstanceData)data.GenerateInstanceData();
inst.Source             = def;
inst.OverrideTransform  = true;
inst.Pos                = beside.Pos + beside.Rot * Vector3.forward;   // ONE TILE IN FRONT
inst.Rot                = beside.Rot;
inst.FactionDef         = host.TacticalFaction.TacticalFactionDef;
inst.MissionParticipant = host.MissionParticipant;
inst.AIActorData        = new AIActorData();
TacticalActor a = ActorSpawner.SpawnActor<TacticalActor>(set, inst);
a.AddonsManager?.SetRagdollMode(CollidersRagdollActivationMode.Targeting);
```

Notes, each of which is a trap someone already paid for:

- **Clone, never mutate the installed def.** `TacCharacterData.Clone` is the game's own copy, so a
  measurement cannot corrupt the def under test.
- **`Strength <= 0` means BORN DEAD.** `Health.Max` is built from bodypart aspects
  (`CharacterStats.InitStats:136-163`); a bodypart-free template enters play at 0/0,
  `FinalizeEnterPlay:546-548` sees `IsDead` and runs `PostProcessDeath`, which switches renderers and
  colliders **off**. Every collider assertion would then be measuring a corpse and failing for an
  unrelated reason. The gate forces `20` only when the template ships none, and reports the template's
  own shipped value separately so the number stays honest.
- **Faction: the creature joins the HOST's faction**, and is placed next to a **hostile**.

### Picking who to stand next to `[SRC]`

```csharp
// Anyone(tac)  -> first live TacticalActor in any faction: a place to stand and a faction to join
// Hostile(tac, of) -> first live actor in a faction where
//                     of.TacticalFaction.GetRelationTo(f) == FactionRelation.Enemy
TacticalActor host = Anyone(tac);
TacticalActor foe  = Hostile(tac, host);
actor = Spawn(def, host, foe ?? host);
```

Both helpers wrap `f.Actors` in `try/catch` and skip on throw — faction actor lists are not always
safe to enumerate.

### The two positioning traps `[SRC]` — both recorded as *measured*

1. **Spawning on top of a friendly offers no targets and hangs the swing.**
   `BashAbility.BashCrt:429-433` computes `forward = target.Actor.Pos - Pos` and then blocks on
   `TacticalNav.Face(forward)`, which **never resolves for a zero vector** — the bash sat there until
   the arm's own 45 s deadline. Hence `beside.Pos + beside.Rot * Vector3.forward`: one tile in front
   gives a non-zero facing direction *and* stays inside every melee range there is.
   Separately, a bash against a **friend** offers no targets at all — `GetTargets` returned 0 and the
   arm could not even start. That is why the neighbour is a hostile.
2. **An unattended Animator is culled and fires no animation events.**
   Every wait inside a bash is a named animation event. With nobody watching, Unity culls the animator
   and none of them fire. The game has the same problem and the same answer one line before it waits
   for `"Ragdoll"` (`RagdollDieAbility.cs:92-95`):
   ```csharp
   if (actor.Animator != null)
       actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
   ```
   This is the harness standing in for the camera a player would have pointed at the creature. It is
   why **death worked unattended while every attack sat there**.

### Making the creature hittable `[SRC]`

Character colliders are live **only while something is targeting** — the game's own rule
(`Addon.RefreshCollidersRagdoll:1502-1504`).

```csharp
targetable = actor.TacticalPerceptionBase;
targetable.RequestForceTargetable();
yield return new WaitForFixedUpdate();      // physics must see the enabled collider
...
finally { targetable.ClearForceTargetable(); }
```

Do **not** write `SetRagdollMode` directly and expect it to stick:
`TacticalPerceptionBase.RefreshCollidersMode:90-107` puts the manager straight back to `InactiveAll`
whenever `ForceTargetable.RefCount` is 0, so a bare mode write is undone within the frame (measured).
`RequestForceTargetable:66-79` is the game's own "something is aiming at this actor" and holds a
refcount — which is exactly the state a shot is resolved in. **Release it**: past six outstanding
requests `RequestForceTargetable:69-72` logs an error.

---

## 7. Ordering actions and waiting for them `[SRC]`

### Attack

```csharp
TacticalAbilityTarget[] offered = bash.GetTargets().ToArray();
TacticalAbilityTarget   tgt     = offered.FirstOrDefault(t => t.Actor == foe)
                               ?? offered.FirstOrDefault(t => t.Actor != null);
bash.Activate(tgt);
```

**Take the target from `GetTargets()`. Never construct one.** A hand-made
`new TacticalAbilityTarget(actor)` sets `Actor` / `GameObject` / `ActorGridPosition` and leaves
**`DamageReceiver` NULL** (`TacticalAbilityTarget.cs:64-70`), and `BashAbility.GetEffectTarget:524-547`
dereferences exactly that field. Measured symptom: *the swing played all the way to the end and then
threw inside `ApplyPayloadEffects`, so no damage landed.* `GetTargets` is the list the UI and the AI
both pick from, and its entries carry the receiver.

Damage then lands the game's own way:
`BashAbility.OnExecute -> ApplyPayloadEffects:553 -> DamagePayload.AccumulateDamage:576 ->
DamageReceiverImplementation.ApplyDamage:108`.

### Waiting, and what a stall looks like

```csharp
while (blew == null && bash.IsExecuting && Time.realtimeSinceStartup - t0 < 45f) {
    // sample Animator.GetCurrentAnimatorClipInfo(0)[0].clip.name once a second,
    // append to `seen` only when it CHANGES
    yield return null;
}
```

- Poll `ability.IsExecuting`, with a hard deadline (45 s here).
- **Sample what the animator is playing while you wait.** A stalled bash is always one of two things —
  the Action state never entered, or it entered playing a clip with no events in it — and *only the
  clip name tells them apart*.
- Every blocking wait is a named animation event with a **10 s** timeout
  (`AnimEventReceiver.cs:100,126`). A bash waits on `ActionDo`, then `ShootShot`, then `ActionEnd`.
- Pass threshold used: `took < 9f`, i.e. **under one missed event**.

`[RAN]` A real stall, verbatim:

```
C1-attack FAIL 'Swarmer_1' bashed 'Fishman_12' 1,00 tile(s) away, chosen from 1 GetTargets offer(s):
Health 190,0 -> 130,0 in 23,24s, animator played [spider_spider_idle]
<- STALLED: each 10s is one animation event the attack clip does not carry (AnimEventReceiver.cs:100,126)
```

Read it: damage **did** land (190 -> 130), so targeting was correct; 23.24 s ~= two 10 s timeouts; and
the animator played `spider_spider_idle` — the attack was wired to the wrong clip. After the fix, the
same line read `in 1,51s`. The clip-name sample is what turned "it's slow" into a diagnosis.

### Damage and death — driven directly, no ability needed

```csharp
actor.Health.Subtract(1f);              // the very call TacticalActorBase.ApplyDamageInternal:874 makes
actor.Health.Subtract(actor.Health);    // -> OnHealthChange:616-622 is the ONLY route to Die()
yield return null;
```

Then watch for 4 s, sampling the playing clip each frame:

```csharp
while (Time.realtimeSinceStartup - d0 < 4f) { /* append Playing(actor) on change */ yield return null; }
```

Four seconds is **longer than any death animation and shorter than one missed event**, so a clip list
that never leaves the idle *is* the ten-second stall showing.

---

## 8. Reading state back — the assertions I relied on `[RAN]`

Every arm is one line: `<name> PASS|FAIL <detail>`, via
`Check(log, arm, ok, detail)` (`CreatureGate.cs:585-589`), summed into a failure count and closed with
`ct_creature: C1 arms PASS` / `ct_creature: C1 <n> FAILURE(S)` / `ct_creature: C1 VOID`.

| Arm | What it proves | How the number is obtained |
|---|---|---|
| `C1-collider` | hittable | colliders on `UnityLayers.Characters.Index` and `.CameraCollider.Index`, vs `ModelBounds` (union of enabled renderer bounds) |
| `C1-shot` | a raycast finds it | `Physics.Raycast` on layer Characters |
| `C1-hover` | the cursor can pick it | `TacticalActorViewBase.SelectionColliders` count |
| `C1-receiver` | damage has somewhere to go | `GetComponentInParent<IDamageReceiver>` on the hit collider |
| `C1-aim` / `C1-vision` | aim + LOS | `GetAimPoint()` distance to model; `GetAimPoints()` non-null count |
| `C1-hp` | stats survived | `Data.Strength` as shipped, spawned `Health.Max` |
| `C1-melee` | the AI can attack | `AIActionMoveAndAttack.GetAttackAbility:73-86` resolves to a named ability |
| `C1-attack` | it lands, and fast | victim `Health` before/after, elapsed seconds, animator clip sequence |
| `C1-damage` | health is writable | `Health.Subtract(1f)` before/after |
| `C1-death` | death is *played* | animator clip sequence over 4 s |
| `C1-kill` | `Die()` was reached | `actor.IsDead`, `actor.GetPreferredDieAbility().GetType().Name` |

`[RAN]` Green example:

```
C1-attack PASS 'Swarmer_1' bashed 'Fishman_12' 1,00 tile(s) away, chosen from 1 GetTargets offer(s):
  Health 190,0 -> 130,0 in 1,51s, animator played [spider_spider_idle]
C1-death PASS the animator played [spider_spider_attack -> spider_spider_death] in 4,01s
C1-kill  PASS Health -> 0,0, IsDead=True, die ability='RagdollDieAbility'
ct_creature: C1 arms PASS
```

**Locale warning `[RAN]`:** the game formats floats with the **machine's** decimal separator — the logs
above say `190,0` and `1,51s`, not `190.0`. A parser must accept both, or normalise before parsing.

### Numbers that are not arms

`[RAN]` The bake prints derived measurements as plain log lines; these were my evidence for a
speed fix:

```
clip 'Spider_Walk' pace: 0.509658 -> 5.4284 tile/s, so the clip plays x10.65107
  (0.833333 s -> 0.078239 s per cycle = 12.78129 cycle(s)/s, sample rate 255.6258 Hz).
clip 'Spider_Walk': ramp 1085.68/s: ... -> the game measures 5.4284 tile/s (TacticalMap.cs:67 TileSize=1)
```

Tiles/second is computable **offline** from a baked clip — `Speed = |Offset| / clip.length`
(`AnimationInfos.cs:123`), and `TacticalMap.cs:67 TileSize = 1f` makes a world unit a tile. A CLI does
not need the game to measure traversal speed; it needs the game to prove the *rest*.

---

## 9. The deploy-then-bake ordering trap `[RAN]`

`deploy.ps1` copies each demo's `Dist\` folder **from the repo INTO the install**. autogate calls it
once at the top, before phase 1.

Therefore:

```
deploy  ->  bake (writes a NEW bundle into the INSTALL)  ->  copy it BACK to the repo  ->  commit
```

Bake before deploy, or forget the copy-back, and the next deploy silently restores the **stale**
bundle over your fresh one. Nothing errors; the gate just measures the old asset.

`[RAN]` I verified the copy-back by size and hash:
`install 345475 B (00:17)` vs `repo 345467 B (17:42)` -> copied -> `repo == install` hash equal.

Note also that `deploy.ps1` deploys **all** demos, not just the one under test.

---

## 10. Gotchas — the list that cost me time

Ordered by how much.

1. **A stale DLL reports green.** Always check the build stamp
   (`expecting build=<x>` / `confirmed build=<x>`). Without it, "my fix didn't help" and "my fix never
   ran" are indistinguishable. `[SRC]`
2. **Baking before deploying silently reverts your bundle.** See §9. `[RAN]`
3. **An unnamed gate measures the wrong unit.** `ct_creature gate 4` picks the first collider-less
   template — a vanilla Fireworm — and reports unrelated failures. Pass the fragment:
   `ct_creature gate 4 ct_creature_morgott`. `[SRC]`
4. **A hand-built `TacticalAbilityTarget` has a null `DamageReceiver`.** The animation plays perfectly,
   then throws, and no damage lands. Take targets from `GetTargets()`. `[SRC]`
5. **Unattended animators fire no events.** Set `AnimatorCullingMode.AlwaysAnimate` or every
   event-blocking action eats 10 s per wait. Death appeared to work only because
   `RagdollDieAbility.cs:92` already sets it. `[SRC]`
6. **Spawning on top of a friendly** -> `Face(zero)` never resolves and `GetTargets` returns 0. Spawn
   one tile in front of a **hostile**. `[SRC]`
7. **A bodypart-free template spawns dead** (Health 0/0), and every collider arm then measures a
   corpse. Force `Strength` for the measurement, report the shipped value separately. `[SRC]`
8. **A bare `SetRagdollMode` is undone within the frame** unless a `ForceTargetable` refcount is held —
   and the refcount must be released. `[SRC]`
9. **DONE can fire while a gate is still measuring** unless the command registers in
   `Dev.AsyncGate.Pending`. The harness then kills the game mid-run and you read a blank as a pass. `[SRC]`
10. **Two runs racing over one log file.** The log name used to be `ct-autogate-phase<N>.log` for every
    agent on the machine; a parallel run truncated another's evidence, and an empty file reads exactly
    like "the gate printed nothing" (2026-08-12). The name now carries the install and the script PID. `[SRC]`
11. **Killing by process name kills the author's game.** Only ever stop the PID you launched. `[SRC]`,
    and `[RAN]` — I refused to run at all while the user's own session was open.
12. **Log lines can be tens of KB.** One arm printed 5461 vertex indices on a single line. Truncate
    every extracted line or you will blow a context/buffer on a *passing* run. `[RAN]`
13. **Stripping too much of the Unity prefix eats the arm name.** An older rule cut everything up to
    the last `| `, which silently removed the gate name from every arm whose summary contains a pipe —
    a FAIL would have printed as an anonymous fragment. Strip only
    `^\[\w+\] \d+ \([^)]*\): `. `[SRC]`
14. **`VOID` is not `FAIL`.** It means *nothing was measured*. Any result filter that omits `VOID`
    turns an unmeasured run into a silent pass. `[SRC]`
15. **Decimal commas.** `190,0` not `190.0`. `[RAN]`
16. **A failed mod load empties `MOD_ACTIVATED`**, disabling every other mod — the harness then looks
    broken for an unrelated reason. `[PRIOR]`

---

## 11. Minimum contract for a CLI built on this

1. Resolve install root; verify `PhoenixPointWin64.exe` and `Mods\ContentTool\ContentTool.dll` exist.
2. **Refuse** if a PP process is running whose `ExecutablePath` equals this install's exe. Never kill
   by name; keep the `-PassThru` PID as the only killable handle.
3. Snapshot `Options.jopt`; restore byte-exact on exit.
4. Deploy first, then bake, then copy generated `Dist` artefacts back to source.
5. Compute the expected DLL SHA-1 prefix (8 hex chars) before launching.
6. Per phase: write `autorun.txt` (UTF-8, one command per line, `#` comments allowed) ->
   `Start-Process <exe> -ArgumentList '-mods','-logFile',<unique per-install-per-PID path> -PassThru`.
7. Poll the log for the init line `ContentTool \d.*build=([0-9a-f]{8})` within `InitTimeoutSeconds`;
   abort if absent (usually: not launched with `-mods`).
8. Poll for `ct_autorun: DONE` within `TimeoutSeconds`; then kill the PID.
9. Compare stamped build to expected; a mismatch invalidates the whole phase.
10. Extract results with a narrow regex including `VOID|REFUSED|THREW`, strip only the Unity prefix,
    truncate each line, and parse decimal commas.
11. Always delete `autorun.txt` in a `finally`.
12. Anything that writes assets goes in an earlier phase than anything that reads them.

---

## 12. Deliberately not covered

Things I did **not** verify this session, listed so nobody mistakes them for tested:

- `CT_AUTORUN` env-var override (I only used the default file path).
- `-KeepOpen`, `-NoDeploy`, and non-default `-TimeoutSeconds` / `-InitTimeoutSeconds`.
- ~~Driving `D:\PP-Instance2`~~ **UPDATE 2026-08-25:** `D:\PP-Instance2` has been driven successfully
  at least four times by `ppcli.ps1` (deploy, ping, a 3-job batch, and a one-job batch). The collision
  guard, profile activation, and build-stamp check all work against that install. See `PPCLI\README.md`
  for the operating manual and measured results.
- The absolute `Application.persistentDataPath` value, and the exact on-disk layout / byte format of
  `Options.jopt`. I never opened or wrote either; §2's claims about them are `[PRIOR]`.
- Any command other than `ct_project <mod>` and `ct_creature gate <save> <fragment>`. The default
  `-Commands` list names seven more (`ct_bake`, `ct_audio`, `ct_texswap`, `ct_meshswap`,
  `ct_scan gate`, `ct_liveswap`); I ran none of them.
- ~~Moving a unit along a path on command.~~ **DONE 2026-08-25** — see §12.1. The gate still spawns
  adjacent so the *attack* arms involve no pathing, but the walk now has an arm of its own.

### 12.1 Issuing a move order and waiting on it — the verified recipe

Built as `C1-walk` in `ContentTool\src\Tactical\CreatureGate.cs`. It caught a real defect on its
first run, which is the point of it.

- **Destination — never invent one.** `MoveAbility.GetTargetsDataInRange(pathRequest, range)`
  (`MoveAbility.cs:202-207`) enumerates only positions the actor can actually path to, so the arm
  never has to decide whether a tile is walkable. `range` is a **path length in tiles**, not AP
  (`MoveAbility.cs:174-179`). Pass `null` for `pathRequest`. Take the **furthest** offer, or a
  half-tile shuffle satisfies an arm about travelling.
- **Target — `MoveAbilityTargetData.ToTarget()`.** Do not hand-build a `TacticalAbilityTarget`;
  `MoveAbility.Activate` reads `PositionToApply` and the hand-made-target trap is already documented
  on the bash arm.
- **Never enumerate mid-ability.** `GetTargetsData` logs an error and invalidates the situation cache
  if the actor has an executing ability (`MoveAbility.cs:170-173`). Wait out the previous arm first.
- **Action points.** Under 1 AP, Move is disabled (`MoveAbility.GetDisabledStateInternal:94`). After
  a bash the creature has spent its turn, so the harness restores
  `actor.CharacterStats.ActionPoints.Set((float)…ActionPoints.Max)`.
- **Completion is NOT new ground after all.** It is the same signal the bash arm already used:
  `move.IsExecuting` stays true until the `PlayingAction` ends, and
  `MoveAbility.OnPlayingActionEnd:83-90` cancels navigation as it closes. Loop on
  `IsExecuting` with a wall-clock timeout.
- **`Animator.cullingMode = AlwaysAnimate` is mandatory.** Nobody is watching, and an unwatched
  Animator fires no animation events — the same reason the attack arms needed it.
- **Assert two things, not one.** Distance travelled (≥2.5 of 3 tiles) *and* a clock ceiling derived
  from `Treadmill.ShippedPace` (5.4284 tile/s). Distance alone passes a walk that is 125× too slow;
  the clock alone passes a creature that teleports.
- **Sample the animator once a second while waiting.** A stall is either "the state was never
  entered" or "it entered a clip that never ends", and only the clip names separate them. A timeout
  landing on a multiple of ~10s is the signature of a blocking animation event the clip does not
  carry (`AnimEventReceiver.cs:100,126`).
- **Print the navigation preconditions in the failure line.** This is what actually found the bug on
  the first instrumented run, and it beat bisecting to it. A move that is accepted and then never
  travels is almost always a missing precondition, not a broken clip, so the arm reports
  `ActiveNavigationClips.Run[Start|Loop|Stop]`, `UsesTurnAnimations` and `ShouldTurnInPlaceBeforeSprint`
  beside the distance. `ActiveNavigationClips` is a *resolved* anim action
  (`TacActorAnimActions.cs:42,53`) and can be **null**; `TacticalPathProcessor.GetRunForwardAnim:207-216`
  refuses outright when `Run.Loop` is null. In the real failure it was neither null nor empty — all
  three slots held the creature's **idle** clip, which has no root motion, so navigation played an
  animation that travels nowhere and waited for a displacement that could never arrive.

**The lesson worth carrying:** `travelled 0.00 tiles` alone does not name a cause. The arm only became
useful when it printed *what the navigation path was holding*. Instrument the seam before bisecting to
it — one run named the defect that five bisect runs would only have narrowed to a commit.

### 12.2 Ordering a move that crosses a level change

Flat ground never touches `ClimbPathProcessor` at all, so a move arm on open floor cannot see any
climb/drop/vault defect. Built as `C1-traverse`; same shape as §12.1 plus two things:

- **Find a non-flat destination.** `GetTargetsDataInRange(null, 12)` and keep candidates whose
  `Position.y` differs from `actor.Pos.y` by more than ~0.5. Three tiles is rarely enough to reach a
  level change; 12 gave 341 offers on the gate save, of which one was 0.56 above.
- **Assert the SEGMENT, not just arrival.** Arriving proves nothing — the game may simply have walked
  round, and the arm would pass while the interesting code never ran.
  `NavMeshPathRequest.GetLinkForSegment:44-51` returns the `NavLink` for a segment and **null for
  ordinary ground**, so counting non-null links over `actor.TacticalNav.CurrentTacPath` is the game's
  own answer to "was this a climb". Read it *during* the walk: the path is built at activation and
  torn down when the move ends, so a read afterwards gets nothing.
- Assert arrived **and** height changed **and** `links > 0` **and** a clock ceiling. A hang is 30 s of
  nothing and separates cleanly from a climb that is merely slower than a walk.
- If no candidate differs in height, report **VOID naming the save**, never a pass on flat ground.

**Measured caveat — do not over-trust this arm.** Falsified by disabling the fix it was meant to
guard: it passed *either way* (bug present: 2.23 s, 1 link of 12; fix applied: 13.09 s, 2 links of 13).
A 0.56 step is a link, but it is not the link type that hangs. An arm that passes whether or not the
bug is present is worth very little — if you extend this, get a save whose spawn reaches a real vault
(`JumpOverLowWall`, `ClimbUpLowObstacle`) rather than tightening the clock on a step it already clears.
