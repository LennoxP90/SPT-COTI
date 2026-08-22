# COTI addon: C11 - True North

Adds COTI mounting support for the night vision goggles from **C11 - True North**.

| File | Goggle |
|---|---|
| `com.c11.truenorth4_anpvs5a.json` | ITT AN/PVS-5A Night Vision Goggles |
| `com.c11.truenorth4_argus_chimera.json` | Argus Chimera Panoramic Bridge (Tan) |
| `com.c11.truenorth4_argus_chimera.json` | Argus Chimera Panoramic Bridge (MultiCam) |
| `com.c11.truenorth4_argus_chimera.json` | Argus Chimera Panoramic Bridge (MC Alpine) |
| `com.c11.truenorth4_dtnvs.json` | ACTinBlack DTNVS (C11 - True North) |

## Installing

Copy the `.json` files into your server's COTI device folder:

```
<SPT>/user/mods/LennoxP90-COTI/nvghostcompat/
```

Loose, no subfolder.

Every file here is prefixed `com.c11.truenorth4_`, so it is obvious which mod it
belongs to, and uninstalling means deleting the files carrying that prefix.

Restart the server. Each supported goggle gains a `mod_coti` slot and the COTI mounts with the pose
in the file.

⚠️ The path differs slightly between SPT versions: `SPT/user/mods/...` on 4.0 and
`SPT_Runtime/user/mods/...` on 4.1.

## If you do not have C11 - True North

Nothing breaks and you can leave the files in place. Every device here declares
`requires: com.c11.truenorth4`, so COTI skips it with one line in the log
rather than warning about items it cannot find.

## If you already played without this addon

COTI will have auto-discovered those goggles and written itself a stub with a guessed pose, so they
worked but sat wrong. Installing this takes precedence automatically - a measured pose always beats
a discovered guess - and the log names the superseded stub so you can delete it.

## Why this is separate from COTI

COTI ships poses for the **vanilla** goggles only. These depend on another mod's items, so they
ship separately: a stock install carries no devices it can never use, and a wrong pose here can be
corrected without waiting for a COTI release.

## Retuning a pose

If a pose looks wrong on your install, fix it in game rather than by hand: inspect the goggle with
a COTI attached and use the **COTI Pose** button. Publishing rewrites this file in place. The
thermal circle has its own editor under F12, which saves itself as you adjust. `ADDONS.md` in the
COTI source has the full field reference.
