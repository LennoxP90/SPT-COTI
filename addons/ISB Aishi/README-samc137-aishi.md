# COTI addon: samc137-aishi

Adds COTI mounting support for the night vision devices from **com.samc137.aishi**.

| File | Device |
|---|---|
| `com.samc137.aishi_gpnvg18.json` | L3Harris GPNVG-18 (ISB Aishi) |
| `com.samc137.aishi_pvs31a.json` | L3Harris PVS-31A (ISB Aishi) |

## Installing

Extract this archive over your SPT install, keeping the folder structure. The
files land in:

```
SPT_Runtime/user/mods/LennoxP90-COTI/nvghostcompat/
```

Restart the server. With COTI 3.3.0 or later each supported goggle gains a COTI slot per tube (`mod_coti` on the tube
COTI has always used, then `mod_coti_1`, `mod_coti_3` and `mod_coti_0` where the goggle has those tubes), and every
COTI mounts on its own tube with the pose in the file. An older COTI reads the same files as one COTI on the
`mod_coti` tube, as before.

On SPT 4.0 the same files go in `user/mods/LennoxP90-COTI/nvghostcompat/`, with
no `SPT_Runtime`, so copy them out rather than extracting over the install.

## If you do not have com.samc137.aishi

Nothing breaks. Every device here declares `requires: com.samc137.aishi`, so COTI skips
it with one line in the log rather than warning about items it cannot find.

## If you already played without this addon

COTI will have auto-discovered those devices and written a stub with a guessed
pose, so they worked but sat wrong. Installing this takes precedence: a measured
pose always beats a discovered guess, and the log names the superseded stub.

## Before going back to an older COTI

Take every extra COTI off (the ECOTI L, ECOTI OR and ECOTI OL slots) first. COTI before 3.3.0, and the
older copy of this addon, give each goggle only its `mod_coti` slot, and the game drops a COTI left in a slot that no
longer exists.

## Retuning a pose

If a pose looks wrong on your install, fix it in the mount editor in the server's web UI (`/coti/model`): pick the
goggle, click a tube's COTI and pose it in the inspector, using **Mirror from partner** as a start for the opposite tube. **Save and
push** rewrites this file in place. The circles come from the goggle type and have nothing to tune. `ADDONS.md` in the
COTI source has the full field reference.

---

Device file schema 1. A COTI too old to read it
says so in the log rather than loading the device.

Exported from the COTI 3.0.3 mount editor, 2026-09-08.
