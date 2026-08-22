# COTI addon files

An addon is a JSON file that tells COTI how to clip itself onto one physical night vision
device. Nothing here needs a recompile. Publishing from the in-game editor takes effect
straight away, with nothing restarted; a file you drop in **by hand** is live at the next
server load, because nothing re-reads the folder on its own.

This document is for anyone who wants to add a device COTI does not already support, or
override the pose COTI's own auto-discovery guessed for one.

## Where files live

On the server, every device file sits in the mod's own folder:

```
SPT_Runtime/user/mods/LennoxP90-COTI/nvghostcompat/   (SPT 4.1)
SPT/user/mods/LennoxP90-COTI/nvghostcompat/           (SPT 4.0)
```

One file per **physical device**, not per item. If a device ships in several textures off the
same mesh - a white-phosphor and green-phosphor pair, three camo variants of the same
goggle - that is one file with several entries in `hosts`, because they share one pose.

The file name does not matter to COTI; the `device` field inside it is the real identity.

## The file, field by field

```json
{
  "schema": 1,
  "device": "argus_chimera",
  "displayName": "Argus Chimera Panoramic Bridge",
  "requires": "com.c11.truenorth4",
  "tuned": true,
  "hosts": [
    { "id": "69e29e097259deabbcff1884", "prefab": "chimera.bundle",     "label": "Tan" },
    { "id": "69e3d5854119e71c8cff1885", "prefab": "chimera_mc.bundle",  "label": "MultiCam" },
    { "id": "69e3d59d816b7c8821ff1887", "prefab": "chimera_mca.bundle", "label": "MC Alpine" }
  ],
  "mask": { "centerX": 0.5, "centerY": 0.5, "radius": 0.28, "feather": 0.01 },
  "mount": {
    "anchorBone": "axis",
    "positionX": 0.0,   "positionY": 0.0,   "positionZ": 0.0,
    "rotationX": -90.0, "rotationY": 0.0,   "rotationZ": 0.0,
    "rollDegrees": 0.0, "pitchDegrees": 0.0, "yawDegrees": 0.0,
    "scale": 1.0
  }
}
```

| Field | Meaning |
|---|---|
| `schema` | Format version of this file. Currently always `1`. See "Schema 1 is permanent" below. |
| `device` | A short, unique identity for the device. Also the log-line name, and (via the in-game editor) the file name. Must be unique across every loaded file - a duplicate is skipped, not merged. |
| `displayName` | The name shown in the editor and in log lines. Can be anything readable. |
| `requires` | Optional. A mod guid - see below. Omit it entirely for a device that needs no other mod (every device COTI ships itself omits it, since none of them depend on a third-party mod). |
| `tuned` | `false` on a stub nobody has posed yet, `true` once a human has confirmed the pose in a raid. An unposed device is still usable - COTI seeds a rough guess - but `tuned: false` says "do not trust these numbers yet." |
| `hosts` | A list of item entries this pose applies to. See below. |
| `mask` | The circular window the thermal overlay renders inside, in normalized 0-1 image coordinates - not metres, not millimetres and not pixels. Editable in game with the **mask editor** (F12 -> Mask Editor -> Open), or by hand here. Start from the closest shipped device rather than guessing. |
| `mount` | Where and how the COTI item attaches to the host's model. This is the part the in-game editor tunes; the panel shows mm and degrees, this file stores metres. |

### `hosts`

Each entry is an object, never a bare string:

```json
{ "id": "69e29e097259deabbcff1884", "prefab": "chimera.bundle", "label": "Tan" }
```

- `id` - the host item's template id (a MongoId). This is how COTI finds the item on a normal,
  healthy install.
- `prefab` - the bundle path the item's `Properties.Prefab.Path` declares. Optional, but see
  below for why it is the single most valuable field to fill in.
- `label` - optional, cosmetic. Only shows up in log lines, useful when a device has more than
  one variant.

#### Why `prefab` matters: it survives a host mod renumbering its items

A third-party mod's template ids are whatever it hardcoded, and an update can renumber them.
If `id` were the only identity, an addon would silently stop working after the host mod
updates: no slot gets injected, and the item quietly stays un-mountable. COTI logs this at
Debug specifically because a supported-but-currently-absent host is the **normal** case (many
hosts come from optional mods that may not be installed), so nothing would call out that this
one broke.

`prefab` fixes that. The pose is a function of the **mesh**, not of the id, so if `id` is no
longer found in the database, COTI falls back to searching every item for one whose
`Properties.Prefab.Path` matches `prefab`. If exactly one item matches, COTI re-binds the
device to that item's new id and logs the change so you can update the file. If more than one
item shares that prefab path, the match is ambiguous and COTI skips it rather than guessing
wrong.

Fill in `prefab` for anything you did not get directly from COTI's own auto-discovered stub.
It is what keeps your addon working across the host mod's future updates without you having to
republish it.

One caveat, and it is correct rather than a mistake: a prefab shared by two hosts of the **same**
device cannot be used for recovery either. COTI's own `dtnvs.json` gives both phosphor variants
the same prefab, because they really are the same mesh - so if that pair's ids ever changed, the
fallback would find two matches and skip, exactly as it would for two unrelated items. Ambiguity
is ambiguity regardless of who owns the matches, and adopting one of two candidate ids by guess
would be worse than declining. A device whose variants each have a distinct prefab is the case
`prefab` can rescue.

If two **separate** device files lay claim to the same item - one naming its id exactly, the
other arriving at it by prefab fallback - the file whose name sorts earlier wins it, and the
loser is named in a warning. So fill in `prefab` for accuracy, not as a claim: it is a recovery
route, and it does not outrank an exact `id` in another file.

⚠️ If your device is the one that lost a host, **do not publish it from the pose editor until
the collision is resolved.** A refused host is absent from what the editor holds, and publishing
writes that back over your file, so the contested entry is dropped from it. COTI keeps one
`.bak` beside the file; a second publish overwrites that too. Fix the collision first - usually
by correcting whichever `id` went stale - then publish.

### `requires`, and how to find a mod's guid

`requires` names a **mod guid** - the same guid the mod's own metadata declares, not its
display name or its file name. At load, COTI checks the guid against the server's list of
loaded mods and skips the whole device (logging which guid was missing) if that mod is not
present.

This is not what makes the device work - if the host mod is absent, its items are not in the
database either way, so the device would be skipped regardless. What `requires` buys is a
**precise diagnosis** instead of a vague one, and it prevents a coincidental prefab match
against some unrelated item that happens to share a bundle name.

To find a mod's guid, start the server and look at its startup log. Every loaded mod logs a
line naming its guid directly, in the shape:

```
... (GUID: com.c11.truenorth4 | targets SPT: ...)
```

Copy the guid exactly as printed. The check is case-insensitive, but match it anyway.

**COTI's own shipped devices omit `requires`, because they are all vanilla goggles.** Anything
that depends on another mod's items is distributed as an addon instead, and every one of those
declares its guid. An earlier version of this document claimed no built-in device depended on a
third-party mod while two of them quietly did, which is how a wrong guid ended up shipped - so
if you are packaging a device that comes from another mod, set `requires` to that mod's guid.
Omitting it when you should not
trades a clear "requires X, not loaded" skip for a confusing prefab-ambiguity warning (or
worse, no warning at all) if something else in the database happens to share the bundle path.

### Finding a host mod's template ids

The easiest way is to let COTI find them for you. Install the host mod, start the server, and
if its items sit under the vanilla NightVision node (true for essentially every real NVG,
including a modded clone), COTI's auto-discovery writes a stub file for it automatically, with
`id` and `prefab` already filled in from the live item table. Open that stub in
`nvghostcompat/`, copy the values out, and either tune the stub in place or start your own file
from them.

If a device is not auto-discovered - most likely because it is not classified as an NVG - you
can still find its ids in the host mod's own database files (commonly a
`db/CustomItems/*.json` or similar under the mod's own folder), or in the server's startup log
where the item database is described.

## Naming a device

Two names matter, and one of them has to be globally unique.

```
"device": "com.wtt.cag_dtnvs"        <- source prefix, then the device
```

| Source | Prefix | Example |
|---|---|---|
| Base game item | `vanilla_` | `vanilla_pvs14` |
| Another mod's item | that mod's guid, then `_` | `com.c11.truenorth4_argus_chimera` |

**The `device` name is the one identifier that must not collide with anyone else's.** COTI dedupes
devices by it, and a publish writes `<device>.json`, so two authors independently shipping
`device: "dtnvs"` means one of them is silently skipped with a duplicate-device warning. No folder
or file naming scheme can prevent that, because the collision is inside the file. A mod guid is
unique by construction, which is why it makes the prefix.

It is not hypothetical: COTI's own C11 DTNVS device had to be called `dtnvs_c11` by hand purely to
avoid colliding with WTT-CAG's identically named one. Under the convention both are unambiguous
without a workaround.

**Name the file after the device**, exactly. Publishing writes `<device>.json`, so a file whose
name disagrees with its `device` field gains a second copy the first time anyone republishes it -
two files, one host, and a duplicate warning.

## Where the files go

All of them straight into `nvghostcompat/`, loose:

```
nvghostcompat/
  vanilla_pvs14.json
  vanilla_gpnvg.json
  com.wtt.cag_dtnvs.json
  com.c11.truenorth4_argus_chimera.json
```

No subfolders needed. The guid prefix already says where every device came from, which is what a
folder would have been telling you - and it says it in the log lines and warnings too, where a
folder name never appears. Uninstalling an addon means deleting the files sharing its prefix.

Subfolders still work if you prefer them: the folder is read **recursively**, so a device file is
found anywhere under it. Two rules if you use them:

- **Folders starting with `_` or `.` are skipped entirely.** Use one to park a device you want to
  keep but not load - `_disabled/old-pose.json` is not read.
- **`.bak` files are ignored** wherever they sit. Publishing keeps one beside each file it
  rewrites, and they must not return as duplicate hosts.

A republished device is rewritten **where it already lives**, so publishing a fix to a device in a
subfolder updates it there rather than leaving a second copy at the top level.

## The official addons

COTI ships poses for the **vanilla** night vision goggles only. Support for modded goggles is
distributed separately, under `addons/` in the COTI source, one folder per host mod:

| Addon | Needs | Covers |
|---|---|---|
| `wtt-cag` | WTT - CAG (`com.wtt.cag`) | ACTinBlack DTNVS, both phosphor variants |
| `wtt-contentbackport` | WTT - ContentBackport (`com.wtt.contentbackport`) | AN/PVS-31A |
| `c11-true-north` | C11 - True North (`com.c11.truenorth4`) | Argus Chimera (three variants), ITT AN/PVS-5A, ACTinBlack DTNVS |

They are ordinary device files with nothing special about them - drop the `.json` into
`nvghostcompat/` and restart. They are also the best worked examples to copy: the Chimera file
shows three hosts sharing one pose because they share a mesh, and the C11 DTNVS shows why a
same-named goggle from a different mod still needs its own file (it uses `dtnvs.bundle` where
WTT-CAG's uses `nvg_actinblack_dtnvg.bundle` - different mesh, different pose).

Keeping them out of the mod means a stock install carries no devices it can never use, and a
wrong pose can be corrected without waiting for a COTI release.

## Schema 1 is permanent

SPT 4.0's `2.0.0` is expected to be the **last** release of that line, so a 4.0 addon can never
be re-issued against a newer file shape - there will be no update prompting its author to
"just bump the schema." That means schema 1 has to stay readable by the 4.1 line **indefinitely**.
Any future schema 2 would be 4.1-only and strictly additive (new fields, never repurposed or
removed ones) - a schema 1 file written today must keep working unmodified for as long as COTI
does.

Fields may be added to a later schema. None may be removed or repurposed. Do not build tooling
that assumes a schema 1 file will ever need translating - it is meant to just keep working.

## The inspect, tune, publish, export loop

This is the whole authoring workflow, entirely in-game:

1. **Inspect** the host item (with a COTI already attached to its `mod_coti` slot). A small
   button appears on the inspect window when the item is a supported host and it is carrying a
   COTI - open it to launch the pose editor.
2. **Tune.** The editor shows the live pose next to the saved one, with a preview viewport
   rendering the actual model. Nudge position, rotation and scale with click-and-hold controls
   (they ramp up the longer you hold, and a modifier key gives finer steps), and use the flip
   test - if the host has a flip animation - to confirm the anchor bone travels with the moving
   part rather than staying behind.
3. **Publish.** One button writes the file to `nvghostcompat/` on the server (keeping a `.bak`
   of whatever was there before), fits the `mod_coti` slot onto any host the device declares
   that does not already have it, and reloads the device table. Anyone else connected picks up
   the change the next time they start their client - there is no live broadcast.
4. **Adjust the circle.** The pose editor moves the COTI unit on the goggle. The *circle* the
   thermal image renders inside is a separate thing, and it can only be judged from first person
   with the goggles down - the inspect window's viewport never shows it. So it has its own
   window: **F12 -> Mask Editor -> Open**. Then close F12; the window stays. Drop your goggles
   and adjust it while looking through them.

   Every control has a key as well as a button, because with the cursor locked in a raid nothing
   on screen is clickable. The window draws the keypad as a joystick and labels each cell with
   whatever key is bound to it, so it always tells you the truth even if you rebind:

   | Key | Does |
   |---|---|
   | keypad 8 / 2 | move the circle up / down |
   | keypad 4 / 6 | move it left / right |
   | keypad + / - | bigger / smaller |
   | keypad 7 / 9 | softer / harder edge |
   | keypad 5 | back to what the server holds |
   | keypad Enter | publish |
   | keypad 0 | close |

   Hold Shift for a finer step. Publishing writes the new circle and leaves the mount exactly as
   the server already has it, so the two editors can never overwrite each other.

5. **Export.** COTI has no in-game "export to zip" button yet. To turn your published device
   into a redistributable addon, copy the one file it wrote out of the server's
   `nvghostcompat/` folder and ship that file to other players - they drop it into their own
   `nvghostcompat/` folder and it behaves exactly like any other addon.

A device stays `tuned: false` until you have actually confirmed the pose in a raid - auto-
discovery seeds a rough guess so a new host is never completely un-mountable, but a guess is
not the same thing as a measured pose. Do not hand-flip `tuned` to `true` without having looked
at it.
