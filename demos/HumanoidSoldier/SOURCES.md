# Sources and licences

| asset | source | author | licence |
|---|---|---|---|
| `Content\Models\soldier.glb` | [Tiffany Cox Idle Animation](https://sketchfab.com/3d-models/tiffany-cox-idle-animation-34cb82552dc6440d9498ad61c67b8f73) (Sketchfab) | JasonKills ([sketchfab.com/ITZCLIX2](https://sketchfab.com/ITZCLIX2)) | [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/) |

The same record travels inside the file: the GLB's `asset.extras` holds `title`, `author`, `license`
and `source` exactly as above (generator `Sketchfab-16.53.0`).

**Changes made (CC BY 4.0 requires saying so).** The downloaded model was re-rigged onto Phoenix
Point's skeleton (`tools\ppskel.py`), reposed and given Phoenix Point's 300 retargeted clips
(`tools\ppretarget.py`), then compressed (`tools\ppzip.py`). The mesh and textures are the author's;
the skeleton names, rest pose and animation clips are not in the original download. The steps are
in [README.md](README.md).

`..\ReplaceCharacterBody\Content\Models\body.glb` is a byte-identical copy of this file under the same
licence.
