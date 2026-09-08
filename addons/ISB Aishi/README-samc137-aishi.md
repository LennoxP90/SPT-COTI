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

Restart the server. Each supported device gains a `mod_coti` slot and the COTI
mounts with the pose in the file.

On SPT 4.0 the same files go in `user/mods/LennoxP90-COTI/nvghostcompat/`, with
no `SPT_Runtime`, so copy them out rather than extracting over the install.

## If you do not have com.samc137.aishi

Nothing breaks. Every device here declares `requires: com.samc137.aishi`, so COTI skips
it with one line in the log rather than warning about items it cannot find.

## If you already played without this addon

COTI will have auto-discovered those devices and written a stub with a guessed
pose, so they worked but sat wrong. Installing this takes precedence: a measured
pose always beats a discovered guess, and the log names the superseded stub.

## Retuning a pose

Use the mount editor in the server's web UI, or the **COTI Pose** button in game.
Either rewrites the file in place. `ADDONS.md` in the COTI source has the full
field reference.

---

Device file schema 1. A COTI too old to read it
says so in the log rather than loading the device.

Exported from the COTI 3.0.3 mount editor, 2026-09-08.
