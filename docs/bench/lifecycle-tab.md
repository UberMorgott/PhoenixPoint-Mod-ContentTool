# Build & share in the bench

!!! note "Screenshots updating"
    The bench was redesigned. New screenshots will follow.

Open [the bench](index.md) from a loaded geoscape and choose `Build & share`. Keep this screen open while building, installing, or verifying; those stages need the panel to be painted.

## Select a mod

1. Use `<` and `>` beside `Mod` to select a content mod.
2. Press `Refresh` if you added a mod or changed the available folders.
3. Read the status line before running anything. `(no mod picked)` means you need to select one.

For a fresh build, a project whose patched bundle is already loaded may need a game restart before that copy can be rebuilt.

!!! note "Screenshot updating"
    The new screenshot will show `Build & share` with a selected mod, the four-step build stepper, and `Build & test` ready to press.

## Build and test

The stepper shows `Check files`, `Build`, `Install`, and `Verify install`. Press `Build & test` to run these four stages in order. It stops when a required stage fails or is refused. The button’s line explains why it is disabled.

| Step | What it checks or changes |
|---|---|
| `Check files` | Checks the manifest and declared source locations. It writes nothing. |
| `Build` | Bakes the project through the same producer as `ct_project <project>`. |
| `Install` | Applies patched copies through the same producer as `ct_route7 apply <project>`. |
| `Verify install` | Checks load-back and served targets through the same producer as `ct_route7 verify <project>`. |

Each step shows its own `PASS`, `FAIL`, `VOID`, or untouched mark and the first line of its result. `VOID` means the stage did not establish the result; read its reason. A `*` marks a result made stale by changed inputs.

Open `Details and log` to see freshness, outcome, installation state, and the log tail. Each step has its own `Run` there. Use it after fixing the reason for a failed or stale step.

!!! note "Screenshot updating"
    The new screenshot will show `Details and log` open after a failed build, with the affected step’s verdict, its `Run` button, and the log tail.

## Package for sharing

`Share` is separate from the four build steps. Press `Package for sharing` after testing. `Build, test & package` runs all five stages in one sequence.

A bench package gets a new folder for each run:

```text
%LOCALAPPDATA%\ContentTool\Packages\<projectId>\<yyyyMMdd-HHmmss>-<runId>
```

The console packager, `ct_package <project>`, writes under `<persistentDataPath>\ContentTool\Packaged\<project>`. Inspect and test the staged package before sharing it. See [the lifecycle](../concepts/lifecycle.md) for the full test sequence.

!!! note "Screenshot updating"
    The new screenshot will show a completed four-step build above `Share`, with `Package for sharing` and `Build, test & package` visible.

## When a run needs attention

- Press `Cancel` to request a stop. A stage already completed keeps its result; later stages are skipped.
- If the status says `restart required`, the game loaded a shipped bundle before the redirect. Restart with the mod enabled, then verify the installed copy.
- If a copy is already being served and cannot be rebuilt, restart with the project disabled before rebuilding. To check the copy already served, run `Verify install`.
- `Check files` and packaging do not need the painted panel. `Build`, `Install`, and `Verify install` do.
