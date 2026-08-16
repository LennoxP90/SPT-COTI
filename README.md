# COTI — Clip-On Thermal Imager

A clip-on thermal imager for SPT 4.1. It clamps to the objective of a night vision device and
injects a thermal overlay **into the tube's circle**, so heat signatures stand out while the night
vision image shows through everywhere else — the way a real fused clip-on works, rather than
replacing your view with a thermal one.

Modelled on the Safran DSI AN/PAS-29B.

---

## What it does

- **Fused, not switched.** The thermal image is added inside the night vision circle only. Anything
  cooler than the heat threshold contributes exactly nothing, so the tube image stays readable.
- **Its own power.** `Ctrl+N` toggles the imager independently of the goggles, like the real device's
  own button. The night vision stays on when the thermal goes off.
- **Mounts to four devices** — PVS-14, N-15, GPNVG-18 and PVS-31A — each with its own tuned position.
- **Other players see it on you.** It renders on your head in third person and is hidden from your
  own first-person view, using the game's own mechanism for worn gear.
- **Found where night vision is found.** It spawns in the same containers and loose-loot positions
  as the goggles it clips to, at a fraction of their rate, and is sold by Peacekeeper.

## Requirements

- **SPT 4.1**
- **Borkel's Realistic NVGs — recommended, not required.** Nothing here depends on it. It is
  recommended because it gives night vision the masked, feathered tube the overlay is designed to sit
  inside; on vanilla night vision the effect still works but sits in a plainer picture.

## Installing

Server half → `SPT_Runtime/user/mods/LennoxP90-COTI/`
Client half → `BepInEx/plugins/LennoxP90-COTI/`

Both are needed: the server registers the item, its slot on each night vision device, and the trader
offer; the client does the rendering.

## Building

```
msbuild SPT-COTI.slnx -p:Configuration=Release -p:SptRoot=<path to your SPT client>
```

`SptRoot` is the folder holding `EscapeFromTarkov.exe`; the client half references the game's
assemblies from `EscapeFromTarkov_Data\Managed` and `BepInEx\plugins\spt` beneath it. The server
half needs no game install.

Both projects stage themselves into `dist\<Configuration>\`, laid out so the contents drop straight
into an SPT folder. `bundles\` holds the prebuilt Unity artifacts; the Unity project that produces
them is not part of this repository, since the model it embeds is licensed rather than free.

## Settings

Image and control settings are on the **F12** page. What the item costs and how often it turns up
are server-side, in `SPT_Runtime/user/mods/LennoxP90-COTI/config/config.json` — a server restart applies them.

| Setting | |
|---|---|
| `trader.loyaltyLevel` / `priceUsd` / `buyLimit` | Peacekeeper's offer. Defaults to LL4, $2000, three per profile. |
| `loot.enabled` | Turn off to make the trader the only source. |
| `loot.weightFraction` | Spawn weight relative to the night vision already at each spot. `0.25` makes it a quarter as likely as the goggles themselves. |

The F12 page:

| Section | Setting | |
|---|---|---|
| **Image** | Enabled | Master switch. Safe to toggle any time, including mid-raid. |
| | Heat Threshold | How hot something must be before it shows. Raise it if the overlay washes the picture out; lower it to pick up cooler things. |
| | Overlay Intensity | Brightness of the heat that does show. Lower it if bodies read as solid white blobs rather than shapes. |
| | Outline Mix | 0 is solid hot shapes, 1 is edge-only contours. |
| **Controls** | Power Toggle | Click and press the combination you want. Default `Ctrl+N`. Keep a modifier — EFT does not demand an exact match on its own binds, so a bare `N` would toggle the goggles too. |
| **Debug** | Verbose Logging | Off for normal play. Writes detailed diagnostics to the BepInEx log if you are reporting a problem. |

Deliberately not exposed: the thermal camera's resolution and refresh, the per-device mask geometry,
and the mount poses. Those are not preferences, they are measured values — exposing them mostly
offers a way to break the effect.

## Performance

The imager renders the scene a second time, off-screen at 768x576, and composites the result inside
the tube. That second pass runs only while the device is powered on and clipped to a host — it is
switched off with the device, not merely hidden.

There is nothing to tune for frames, and the settings that look like performance levers are not:
`hz` is the sensor's refresh rate, driving a frame hold that is purely cosmetic, and the render
target is small enough that its size is not the bottleneck.

## Credits

**3D model by [3DMA — 3D Military Assets](https://www.3dmilitaryassets.com/)**, used under their
Extended Licence.

Thanks to Eukyre for pointing out that worn gear wants a dress script — that turned out to be
exactly how the device is hidden from the wearer's own view.

## Licence

The mod's own code is free to use. The 3D model is **not** — it is licensed from 3DMA and is
redistributed only in compiled form inside the asset bundle, as their licence permits. Do not
extract, redistribute, or reuse the model, its mesh or its textures.
