# Codex review #2 — merged fix pass `eb0ffc1..main` (2026-09-28)

Scope: `git diff eb0ffc1..main -- src tests autogate.ps1 deploy.ps1`. Codex = read-only reviewer via agent-link (`cx` run timed out at 600 s). Focused pass, no runtime run; Codex did not claim a full 96-file audit.

## Codex findings (verbatim substance) + Claude verdict

1. HIGH | `src/Project/ModRoster.cs:233-244` | prefix routes installed before dependent DLL `OnModEnabled`; failed `SetEnabled` can leave live registrations while mod is OFF | fix: roll back prefix changes on failed enable.
   - Verdict: CONFIRMED, already in HANDOFF "Deferred — needs in-game" (ModRoster rollback). Not fixed — only the game proves the enable-failure path.
2. MED | `src/Import/GlbReader.cs:1315-1343` | `LeavesRest` compares every CUBICSPLINE output triplet (in-tangent, value, out-tangent) with the rest; zero tangents read as "moves", a rest-holding helper curve is refused.
   - Verdict: CONFIRMED (tangents 0 vs rest quaternion -> dot 0 -> "leaves rest"). FIXED: CUBICSPLINE compares only the value element. Regression case in `tests/ObjCodecTests/ClipImport.cs` (fails on old code).
3. MED | `src/Bake/VideoCatalog.cs:564` | `LiveAt` calls `LoadDeclared(root)` with no refusal sink; `intro.mp4` + `intro.webm` throws instead of SOURCE SKIPPED, ending video enable for the whole mod.
   - Verdict: CONFIRMED. FIXED: `LiveAt` passes a sink, prints `SOURCE SKIPPED: ...` lines, counts them as refused; other clips still served. Runtime proof needs in-game.
4. MED | `src/Project/Package.cs:90-153` | package never validates video rows; missing clip / unknown catalog asset still PACKAGED.
   - Verdict: PARTLY CONFIRMED. Missing clip + same-stem pair: FIXED (`Package.VideoRefusals`, refused before success; `PackageGate` cases). Catalog `asset` resolution needs the game's streamable catalog (Lifecycle bake `VideoRows` checks it) -> needs-game / not offline.

## codex-review-1 status (Codex, same numbering)
1 fixed (UnsafeName + MetaOrRefuse) · 2 fixed (manifest/meta JSON parse, comments + trailing commas) · 3 partial -> now closed offline by finding 4 fix · 4 not fixed = finding 1 (needs in-game) · 5 partial: Codex says "absent declared source with existing bank accepted" — REJECTED as defect: the bank is what ships and plays, source is dropped anyway; ledger-less mtime fallback warns by design · 6 fixed for owned registrations (anonymous Register separate by design; playback needs in-game) · 7 fixed · 8 fixed · 9 fixed · 10 fixed · 11 docs, out of scope · 12 not fixed, architectural (ProjectBake combined pipeline) — no action · 13 workshop VDF, out of scope.

## Deep-check notes (Codex)
DevGate marker file-only, checked at registration; `ct_mission gate` subverb-gated; CatalogOwners lowest-id rule deterministic; UnsafeName rejects CON/NUL/COM1 + separators; sources.ledger SHA-1 keyed by media; no further path-traversal defect found in inspected paths.
