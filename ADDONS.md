# COTI addon files

An addon is a JSON file that tells COTI how to clip itself onto one physical night vision
device, on each of its tubes. Nothing here needs a recompile. Saving from the mount editor
(`/coti/model`, see the end of this file) takes effect straight away, with nothing restarted; a
file you drop in **by hand** is live at the next server load, because nothing re-reads the folder
on its own.

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
  "device": "com.c11.truenorth4_dtnvs",
  "displayName": "ACTinBlack DTNVS (C11 - True North)",
  "requires": "com.c11.truenorth4",
  "tuned": true,
  "hosts": [
    { "id": "69e3d5a9609333ebadff188a", "prefab": "dtnvs.bundle" }
  ],
  "mask": { "centerX": 0.5353, "centerY": 0.4991, "radius": 0.27362, "feather": 0.01 },
  "mount": {
    "anchorBone": "axis_2",
    "positionX": -0.0005, "positionY": -0.0435, "positionZ": -0.038,
    "rotationX": 0.0,     "rotationY": 0.0,     "rotationZ": 0.0,
    "rollDegrees": 0.0,   "pitchDegrees": 0.0,  "yawDegrees": 48.0,
    "scale": 1.065
  },
  "layout": "dual",
  "tubes": {
    "tube_1": {
      "mount": {
        "anchorBone": "axis_1",
        "positionX": 0.0005,  "positionY": -0.0435, "positionZ": -0.038,
        "rotationX": 0.0,     "rotationY": 0.0,     "rotationZ": 0.0,
        "rollDegrees": 0.0,   "pitchDegrees": 0.0,  "yawDegrees": -48.0,
        "scale": 1.065
      },
      "pod": { "downX": -90.0, "downY": 0.0, "downZ": 0.0 }
    },
    "tube_2": {
      "mount": {
        "anchorBone": "axis_2",
        "positionX": -0.0005, "positionY": -0.0435, "positionZ": -0.038,
        "rotationX": 0.0,     "rotationY": 0.0,     "rotationZ": 0.0,
        "rollDegrees": 0.0,   "pitchDegrees": 0.0,  "yawDegrees": 48.0,
        "scale": 1.065
      },
      "pod": { "downX": -90.0, "downY": 0.0, "downZ": 0.0 }
    }
  }
}
```

The numbers are an example. Pose your own in the mount editor, which writes every field here except
`pod`.

| Field | Meaning |
|---|---|
| `schema` | Format version of this file. Currently always `1`. See "Schema 1 is permanent" below. |
| `device` | A short, unique identity for the device. Also the log-line name, and (via the mount editor) the file name. Must be unique across every loaded file - a duplicate is skipped, not merged. |
| `displayName` | The name shown in the editor and in log lines. Can be anything readable. |
| `requires` | Optional. A mod guid - see below. Omit it entirely for a device that needs no other mod (every device COTI ships itself omits it, since none of them depend on a third-party mod). |
| `tuned` | `false` on a stub nobody has posed yet, `true` once a human has confirmed the pose in a raid. An unposed device is still usable - COTI seeds a rough guess - but `tuned: false` says "do not trust these numbers yet." |
| `hosts` | A list of item entries this pose applies to. See below. |
| `mask` | The **legacy circle**: where an older COTI draws its one thermal circle, in 0-1 screen coordinates (x from the left, y from the bottom, radius in screen heights). Mandatory. On a file without `tubes` it is the circle every COTI draws. On a file with `tubes` it must be the home tube's circle at 16:9 (see "The legacy pair is mandatory"), which the mount editor writes for you; COTI 3.3.0 draws its circles from `layout` instead. |
| `mount` | The **legacy mount**: where and how the COTI attaches to the goggle's model, in metres and degrees. Mandatory. On a file with `tubes` it is a copy of the home tube's mount, written by the mount editor; COTI 3.3.0 uses it only for a tube missing from `tubes`, and an older COTI mounts its one COTI with it. |
| `layout` | Optional, COTI 3.3.0. The goggle's tube pattern: `quad`, `dual`, `mono` or `pvs5a`. It decides the COTI slots and where each tube's circle sits. See below. |
| `tubes` | Optional, COTI 3.3.0, and only with `layout`. One entry per tube, keyed by its label, each with its own `mount`, an optional `pod` and an optional `text`. See below. |
| `text` | Optional, COTI 3.3.0, and only on a file without `tubes`: where its one circle's messages sit, as a tube's `text` below (default centred). A file with `tubes` ignores it with a warning, and picking a **Type** for the file in the mount editor does not carry it over to any tube. |

### `layout` and `tubes`

A file with both is a **v2** file. Without them it is **v1**, and loads exactly as it always has:
one COTI slot, one circle from `mask`, one mount.

| Layout | Goggles | Tube labels, left to right | Home tube |
|---|---|---|---|
| `quad` | GPNVG-18, Argus Chimera | `tube_0`, `tube_1`, `tube_2`, `tube_3` | `tube_2` |
| `dual` | N-15, PNV-57E, DTNVS, PVS-31A | `tube_1`, `tube_2` | `tube_2` |
| `mono` | PVS-14, PNV-10T | `tube_center` | `tube_center` |
| `pvs5a` | AN/PVS-5A | `tube_1`, `tube_2` | `tube_2` |

The **home tube** is the one COTI has always mounted on. It keeps the old `mod_coti` slot, so a COTI
already fitted in a profile stays where it is. Every other tube gets a slot of its own. Slots are
added in this order, which is the order a COTI dropped on the goggles, rather than on a slot, fills
them. The inspect window still shows them left to right as the tubes sit (ECOTI OL, ECOTI L, ECOTI R, ECOTI OR):

| Tube | Slot | In game |
|---|---|---|
| home | `mod_coti` | ECOTI R (ECOTI on a mono or a v1 device) |
| `tube_1` | `mod_coti_1` | ECOTI L |
| `tube_3` | `mod_coti_3` | ECOTI OR |
| `tube_0` | `mod_coti_0` | ECOTI OL |

The slots come from `layout` alone, never from which `tubes` entries are valid, so a typo in one
tube cannot take away a slot that holds a COTI.

The circles are not in the file. They are measured from BorkelRNVG's masks, centred in the tube
cutouts at a fixed size and the same for every goggle of a layout, so there is nothing to tune. The circles assume BorkelRNVG's default mask size. A player who changes Borkel's F12 Mask size multiplier moves Borkel's holes and not COTI's circles, most on the outer quad tubes.

Each `tubes` entry has:

- `mount` - the tube's own mount, with the same fields and units as the top-level `mount`.
- `pod` - optional, for a goggle whose pods flip up one at a time:
  `{ "bone": "axis_3", "downX": -33.0, "downY": 0.0, "downZ": 0.0 }`. The `down` values are the pod
  bone's `localRotation` when the pod is down, in Unity Euler degrees as `rotationX/Y/Z` are.
  `bone` is optional and defaults to the tube's `anchorBone`. `downX`, `downY` and `downZ` are all
  required; a missing one reads as 0 with no warning. A pod more than 45 degrees from down
  counts as up, and its tube's circle closes as if the night vision were off for that tube. Write
  pods by hand from the source mod's own data; the mount editor shows them but does not edit them.
- `text` - optional, where the tube's boot, mode and shutdown messages sit:
  `{ "align": "left", "edge": 0.6, "y": 0.0 }`. `align` is `left`, `right` or `center`, the side of
  the text that is anchored; `edge` is that side's distance from the circle's centre in circle radii
  (for `center`, how far the text's middle sits right of the centre); `y` moves it up, in radii.
  Every field is optional and a missing one keeps the rule: left tubes left, right tubes right,
  `tube_center` centred, edge 0.6 (0 for `center`), y 0. No value can push the text off screen.
  The mount editor sets it by dragging the text and writes only the fields that differ from the
  rule. An older COTI ignores it.

When COTI 3.3.0 loads a file it checks these rules in order:

1. If `layout` is missing or unknown, or `tubes` is missing, the file is v1. A warning names the
   file when only one of the two is there, or when the layout is unknown.
2. A label the layout does not have, or a tube with a mount value that is not a finite number, is
   dropped with a warning, and that tube mounts at the legacy `mount`.
3. A pod with a `down` value that is not a finite number is dropped with a warning, and the tube
   follows the goggles only. A `text` block with an unknown `align` or a value that is not a finite
   number is dropped with a warning, and the text sits where the rule puts it.
4. A tube of the layout that is missing from `tubes` mounts at the legacy `mount`.

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
save it again.

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

⚠️ If your device is the one that lost a host, **do not save it from the mount editor until
the collision is resolved.** A refused host is absent from what the editor holds, and saving
writes that back over your file, so the contested entry is dropped from it. COTI keeps one
`.bak` beside the file; a second save overwrites that too. Fix the collision first - usually
by correcting whichever `id` went stale - then save.

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
devices by it, and a save writes `<device>.json`, so two authors independently shipping
`device: "dtnvs"` means one of them is silently skipped with a duplicate-device warning. No folder
or file naming scheme can prevent that, because the collision is inside the file. A mod guid is
unique by construction, which is why it makes the prefix.

It is not hypothetical: COTI's own C11 DTNVS device had to be called `dtnvs_c11` by hand purely to
avoid colliding with WTT-CAG's identically named one. Under the convention both are unambiguous
without a workaround.

**Name the file after the device**, exactly. Saving writes `<device>.json`, so a file whose
name disagrees with its `device` field gains a second copy the first time anyone saves it again -
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
- **`.bak` files are ignored** wherever they sit. Saving keeps one beside each file it
  rewrites, and they must not return as duplicate hosts.

A saved device is rewritten **where it already lives**, so saving a fix to a device in a
subfolder updates it there rather than leaving a second copy at the top level.

## The official addons

COTI ships poses for the **vanilla** night vision goggles only. Support for modded goggles is
distributed separately, under `addons/` in the COTI source, one folder per host mod:

| Addon | Needs | Covers |
|---|---|---|
| `wtt-cag` | WTT - CAG (`com.wtt.cag`) | ACTinBlack DTNVS, both phosphor variants |
| `wtt-contentbackport` | WTT - ContentBackport (`com.wtt.contentbackport`) | AN/PVS-31A |
| `c11-true-north` | C11 - True North (`com.c11.truenorth4`) | Argus Chimera (three variants), ITT AN/PVS-5A, ACTinBlack DTNVS |
| `ISB Aishi` | ISB Aishi (`com.samc137.aishi`) | L3Harris GPNVG-18, L3Harris PVS-31A |
| `wtt-artem` | WTT - Artem (`com.crackbone.artem-wtt`) | GPNVG-18 (White, OD, Black), PVS-31, PVS-31 (Wide) |

They are ordinary device files with nothing special about them - drop the `.json` into
`nvghostcompat/` and restart. They are also the best worked examples to copy: the Chimera file
shows three hosts sharing one pose because they share a mesh, and the C11 DTNVS shows why a
same-named goggle from a different mod still needs its own file (it uses `dtnvs.bundle` where
WTT-CAG's uses `nvg_actinblack_dtnvg.bundle` - different mesh, different pose).

Keeping them out of the mod means a stock install carries no devices it can never use, and a
wrong pose can be corrected without waiting for a COTI release.

## Schema 1 is permanent

SPT 4.0's `2.0.0` is expected to be the **last** release of that line, so a 4.0 addon can never
be re-issued against a newer file shape. That means schema 1 has to stay readable by the 4.1 line
**indefinitely**: a schema 1 file written today must keep working unmodified for as long as COTI
does. Fields may be added; none may be removed or repurposed. Do not build tooling that assumes a
file will ever need translating - it is meant to just keep working.

`layout` and `tubes` are such an addition, made in COTI 3.3.0 without changing `"schema": 1`. Every
COTI that reads device files (3.0.0 to 3.2.0 on SPT 4.1, 2.0.0 to 2.0.2 on SPT 4.0) ignores fields
it does not know, so a v2 file still loads there as one COTI on the home tube, from the legacy
pair. Earlier releases compiled their goggles in and never read this folder.

### The legacy pair is mandatory

`mask` and `mount` stay required on every file, v2 included: every COTI, 3.3.0 included, skips a
file without them. On a v2 file they must describe the home tube, because that is what an older
COTI shows:

- `mount` is a copy of the home tube's `mount`;
- `mask` is the home tube's circle at 16:9, centre at 4 decimals and radius at 5:

| Layout | `centerX` | `centerY` | `radius` | `feather` |
|---|---|---|---|---|
| `quad` | 0.5371 | 0.4992 | 0.28506 | 0.01 |
| `dual` | 0.5353 | 0.4991 | 0.27362 | 0.01 |
| `mono` | 0.5006 | 0.4992 | 0.27359 | 0.01 |
| `pvs5a` | 0.5021 | 0.5056 | 0.24311 | 0.01 |

The mount editor rewrites both on every save. If you edit `tubes` by hand, update the pair too.

### Going back to an older COTI

- A COTI before 3.3.0 that saves the device, from its mount editor or its in-game editor, writes
  the file without `layout` and `tubes`.
- Downgrading COTI below 3.3.0, or turning a file back into v1, removes the extra slots while
  profiles may still hold COTIs in them, and EFT then drops those COTIs. **Take every extra COTI off the
  extra tubes first.** Checked on SPT 4.1.6: a COTI left in ECOTI L when the file went back to v1 showed
  on no slot at login with no error logged, and was gone from the profile after the next raid.

## The mount editor: pose, check, save, export

Device files are written from the server's web UI on SPT 4.1: open SIC and pick
**ECOTI -> Mount editor** (`/coti/model`). From 3.3.0 it is COTI's only editor. The 2.x line for
SPT 4.0 has no mount editor; its own copy of this file describes its in-game editor. It always saves
a file with `layout` and `tubes`; reading files without them, and writing the legacy pair, are there
only so older COTI keeps working.

Click a COTI in the 3D view to edit its mount, or a text in the game screen to edit where it sits;
click anything else to get back to the device (**Type**, **Save and push**, **Revert**, **Export
addon**). The panel on the right shows only what is selected.

1. **Pick the goggle**, and its variant when it has several. The 3D view shows a COTI on every
   tube of the file's layout, posed exactly as the game poses them, the selected one in
   orange. The **Show** checkboxes in its top right corner pick which tubes' COTIs (and circles) are
   drawn; they are a view setting and are never saved. Drag to orbit, scroll to zoom, and click a
   cube face to look down that axis. Switching goggle with unsaved changes asks before discarding
   them.
2. **Type.** The layout comes from the file's `layout` field and nothing else: the editor never
   guesses it from the model, EFT's mask family or BorkelRNVG's config. **Type** shows it and can
   override it (`quad`, `dual`, `mono`, `pvs5a`). The tabs, checkboxes, COTIs and circles follow
   at once; nothing is written until **Save and push**, and poses of tubes the new type lacks are
   kept until then but not saved. A file without `layout` shows **Unknown**: its one COTI is drawn
   at the legacy `mount`, but nothing can be edited or saved until a type is picked. Picking one
   upgrades it: the home tube keeps the current pose, its partner gets the mirror of it, and the
   other tubes start unposed. There is no way back to Unknown once a file has a layout. Saving a
   new type adds its slots. Running games take the poses straight away, but Fika peers and the
   headless see the new slots only after a relaunch.
3. **Pose each tube.** There is one tab per tube. A dashed tab is not posed yet and mounts at the
   legacy `mount` in game; its first nudge poses it. Position, rotation, scale and anchor bone are
   on the right, and the flip test checks that the COTI moves with the part that flips. **Mirror
   from partner** fills a tube from the one opposite it (`tube_1` from `tube_2`, `tube_0` from
   `tube_3`). Sideways position, yaw, roll, base Y and base Z change sign, and the tube keeps its
   own anchor bone when it has one. A tube's pod, if the file declares one, is shown read only.
4. **Check the circles and place the text.** The screen over the corner of the 3D view draws a
   plain 1920 x 1080 game screen (16:10 and 21:9 selectable; **Enlarge** makes it bigger). It shows
   every tube's circle and the longest message in each, placed as the game places it. The circles
   are fixed per layout. Click a text to select it, then drag it, or use the arrow keys (Shift for
   bigger steps); the dashed line is the screen margin it cannot cross. The panel sets its align,
   edge and y directly, and **Reset to default** puts it back on the rule.
5. **Save and push.** This writes the file where it already lives (keeping one `.bak`), rewrites
   the legacy pair from the home tube, fits any slot a host is missing, and re-poses every running
   game straight away.
6. **Export addon.** For a goggle from another mod, this packages every device that names the same
   `requires` into a 7z with a README, ready to extract over an SPT install.

Saving marks the device `tuned: true`, which also makes it the seed for goggles discovered after
it. Auto-discovery's rough guess means a new goggle can always take a COTI, but a guess is not a
measured pose. Check every tube in a raid before shipping a file, and do not hand-flip `tuned` to
`true` without having looked at it.
