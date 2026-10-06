# COTI addon: WTT - CAG

Adds COTI mounting support for the night vision goggles from **WTT - CAG**.

| File | Goggle |
|---|---|
| `com.wtt.cag_dtnvs.json` | ACTinBlack DTNVS (White Phos.) |
| `com.wtt.cag_dtnvs.json` | ACTinBlack DTNVS (Green Phos.) |

## Installing

Copy the `.json` files into your server's COTI device folder:

```
<SPT>/user/mods/LennoxP90-COTI/nvghostcompat/
```

Loose, no subfolder.

Every file here is prefixed `com.wtt.cag_`, so it is obvious which mod it
belongs to, and uninstalling means deleting the files carrying that prefix.

Restart the server. With COTI 3.3.0 or later each supported goggle gains a COTI slot per tube (`mod_coti` on the tube
COTI has always used, then `mod_coti_1`, `mod_coti_3` and `mod_coti_0` where the goggle has those tubes), and every
COTI mounts on its own tube with the pose in the file. An older COTI reads the same files as one COTI on the
`mod_coti` tube, as before.

⚠️ The path differs slightly between SPT versions: `SPT/user/mods/...` on 4.0 and
`SPT_Runtime/user/mods/...` on 4.1.

## If you do not have WTT - CAG

Nothing breaks and you can leave the files in place. Every device here declares
`requires: com.wtt.cag`, so COTI skips it with one line in the log
rather than warning about items it cannot find.

## If you already played without this addon

COTI will have auto-discovered those goggles and written itself a stub with a guessed pose, so they
worked but sat wrong. Installing this takes precedence automatically - a measured pose always beats
a discovered guess - and the log names the superseded stub so you can delete it.

## Before going back to an older COTI

Take every extra COTI off (the ECOTI L, ECOTI OR and ECOTI OL slots) first. COTI before 3.3.0, and the
older copy of this addon, give each goggle only its `mod_coti` slot, and the game drops a COTI left in a slot that no
longer exists.

## Why this is separate from COTI

COTI ships poses for the **vanilla** goggles only. These depend on another mod's items, so they
ship separately: a stock install carries no devices it can never use, and a wrong pose here can be
corrected without waiting for a COTI release.

## Retuning a pose

If a pose looks wrong on your install, fix it in the mount editor in the server's web UI (`/coti/model`): pick the
goggle, click a tube's COTI and pose it in the inspector, using **Mirror from partner** as a start for the opposite tube. **Save and
push** rewrites this file in place. The circles come from the goggle type and have nothing to tune. `ADDONS.md` in the
COTI source has the full field reference.
