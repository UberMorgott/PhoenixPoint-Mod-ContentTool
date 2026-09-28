# Fit a weapon

Use `Fit a weapon` to adjust how a weapon your mod already adds sits in a soldier’s hand. The weapon must be loaded in this game session, which means its mod was enabled when the game started. To make a new weapon first, follow [Add a weapon](add-weapon.md); its step 3 has the same fit controls with sliders.

The step bar reads `Soldier`, `Weapon`, `Adjust`, `Save`.

## Example: adjust your mod weapon in a soldier’s hand

1. Open the [bench](index.md) and choose `Fit a weapon`.
2. Click the button beside `Soldier` to open the soldier list with its find box. Pick the soldier who will hold the weapon. A `*` marks a unit built by a content mod. Press `rescan` if you enabled a mod while the bench was open.
3. Click the button beside `Weapon` to open the weapon list. Type part of your weapon’s name. The hint reads `<n> of <m> weapons fit this soldier   * = made by your mod (<k>)`. A `*` marks a weapon your mod made; `* live` means its fit is loaded this session, so it can be adjusted. Tick `all` to also list weapons this soldier cannot equip.
4. Look at the weapon in the hand. Drag the arrows on the weapon to move it and the rings to turn it about that axis. A dimmed handle is edge-on to the camera; turn the camera to use it. `Esc` cancels a drag. The [camera controls](navigation.md#use-the-3d-view) work as on every 3D screen.
5. `Unequip` takes the weapon out of the hand and `Re-equip` puts it back, so you can compare.
6. When the badge says `Changed - not saved yet.`, press `Save to file *`. It writes the fit into your mod’s `ppcontent.json`; the path is in `Details`. The badge then says `Saved - the file matches what you see.`

## Undo

- `Revert` goes back to what the file says. It does not touch the disk.
- `Reset to automatic` goes back to the measured fit and drops every override. It does not touch the disk either; press `Save to file` to keep it.

## When Save is grey

The line under `Save to file` says what is missing:

| Line | What to do |
|---|---|
| `pick a weapon first` | Pick a weapon in step 3. |
| `this is a game weapon - it is in the hand for comparison only; only weapons your mod builds have a file to save into` | Shipped weapons can stand beside yours for comparison, but have no file to save. Pick your weapon. |
| `wait - the weapon has not loaded in the hand yet (re-pick it if this stays)` | Wait a moment, or pick the weapon again. |

## Console alternative

Give a soldier the weapon in game, then run:

```text
ct_fit <WeaponDef>
ct_fit <WeaponDef> save
```

The first command reports the current fit; `save` writes it to the mod’s `ppcontent.json`.
