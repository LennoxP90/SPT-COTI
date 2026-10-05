# COTI - Clip-On Thermal Imager

A clip-on thermal imager for SPT. Both the server half and the client half support **SPT 4.0** and
**SPT 4.1**. It clamps to the objective of a night vision device and
injects a thermal overlay **into the tube's circle**, so heat signatures stand out while the night
vision image shows through everywhere else - the way a real fused clip-on works, rather than
replacing your view with a thermal one.

Modelled on the Safran DSI AN/PAS-29B.

---

## What it does

- **Fused, not switched.** The thermal image is added inside the night vision circle only. Anything
  cooler than the heat threshold contributes exactly nothing, so the tube image stays readable.
- **Heat is heat.** Only warm things draw: people, a fire, a barrel or suppressor hot from shooting.
  Heat comes from the game's own heat value for each material, not from how bright a surface
  renders, so weapon parts that never get hot and glossy surfaces stay dark.
- **Two modes.** Outline draws the edges of hot things; Full fills them in. `Alt+N` switches between
  them, and the display names the new mode with the calibration click.
- **Through a scope.** With `Magnify With Optic` on, heat is drawn through a magnified scope's own
  lens at the scope's zoom, so it lines up with what the scope shows. Behind a thermal sight such as
  the REAP-IR or the RS-32, the imager stands down and the sight's own picture is left alone.
- **Its own power.** `Ctrl+N` toggles the imager independently of the goggles, like the real device's
  own button. The night vision stays on when the thermal goes off. Powering up shows the device's
  own `Initializing...`, then the thermal arrives with the core's calibration click; powering down
  shows `Power Off...`. Switch the sequence off in F12 for an instant toggle.
- **Mounts to five devices** - PVS-14, N-15, GPNVG-18, PVS-31A and the DTNVS from WTT Clothing and
  Gear - each with its own tuned position. A device whose mod is not installed is skipped silently.
- **Other players see it on you.** It renders on your head in third person and is hidden from your
  own first-person view, using the game's own mechanism for worn gear.
- **Found where night vision is found.** It spawns in the same containers and loose-loot positions
  as the goggles it clips to, at a fraction of their rate, and is sold by Peacekeeper.

## Requirements

- **SPT 4.0 or SPT 4.1.** Both generations get the full mod - server and client.
- **Borkel's Realistic NVGs - recommended, not required.** Nothing here depends on it. It is
  recommended because it gives night vision the masked, feathered tube the overlay is designed to sit
  inside; on vanilla night vision the effect still works but sits in a plainer picture.

## Installing

Server half → `SPT_Runtime/user/mods/LennoxP90-COTI/` (4.1) or `SPT/user/mods/LennoxP90-COTI/` (4.0)
Client half → `BepInEx/plugins/LennoxP90-COTI/` (same path on both versions)

Both halves are needed on either version: the server registers the item, its slot on each night
vision device, and the trader offer; the client does the rendering.

## Building

Building the server half needs reference assemblies from **both** an SPT 4.0 install and an SPT 4.1
install - it targets both `net9.0` and `net10.0` and validates both reference sets up front:

```
dotnet build Coti.Server -c Release -p:SptRef40Dir=<path to your SPT 4.0 install> -p:SptRef41Dir=<path to your SPT 4.1 install>
```

If you only have one install, build just that target framework instead of failing on the other:

```
dotnet build Coti.Server -c Release -f net10.0
```

The client half builds against a live game install and takes the SPT generation as a property.
Both client builds are `net472`, so MSBuild cannot multi-target them - build the project twice,
once per version:

```
msbuild Coti.Client\Coti.Client.csproj -p:Configuration=Release -p:SptTarget=4.1
msbuild Coti.Client\Coti.Client.csproj -p:Configuration=Release -p:SptTarget=4.0
```

`SptTarget` defaults to `4.1` if omitted. `SptRoot` defaults sensibly for each target
(`F:\Games\SPT-4.1\SPT-4.1-client` and `F:\Games\SPT-4.0\Clean` respectively) but can be overridden:

```
msbuild Coti.Client\Coti.Client.csproj -p:Configuration=Release -p:SptTarget=4.0 -p:SptRoot=<path to your SPT 4.0 client>
```

`SptRoot` is the folder holding `EscapeFromTarkov.exe`; the client half references the game's
assemblies from `EscapeFromTarkov_Data\Managed` and `BepInEx\plugins\spt` beneath it. The two
builds compile different `#if` paths against different, differently-obfuscated game assemblies - their
output DLLs are never byte-identical, and the build stages each one into its own
`spt-4.0`/`spt-4.1` root so one target can never overwrite the other.

Both projects stage themselves into `dist\<Configuration>\spt-4.0\` and `dist\<Configuration>\spt-4.1\`,
laid out so the contents of each drop straight into the matching SPT folder. `bundles\` holds the
prebuilt Unity artifacts; the Unity project that produces them is not part of this repository, since
the model it embeds is licensed rather than free.

## Settings

Image and control settings are on the **F12** page. What the item costs and how often it turns up
are server-side, in `SPT_Runtime/user/mods/LennoxP90-COTI/config/config.json` - a server restart applies them.

| Setting | |
|---|---|
| `trader.loyaltyLevel` / `priceUsd` / `buyLimit` | Peacekeeper's offer. Defaults to LL4, $2000, three per profile. |
| `loot.enabled` | Turn off to make the trader the only source. |
| `loot.weightFraction` | Spawn weight relative to the night vision already at each spot. `0.25` makes it a quarter as likely as the goggles themselves. |
| `flea.playerSellable` | Off by default, so the only flea listing is Peacekeeper's own, at `priceUsd`. On also lets SPT list simulated player offers and lets players sell their own. SPT prices those listings itself, not from `priceUsd`: expect roughly two to three times Peacekeeper's price. |

The F12 page:

| Section | Setting | |
|---|---|---|
| **Image** | Enabled | Master switch. Safe to toggle any time, including mid-raid. |
| | Heat Threshold | How hot something must be before it shows. Raise it if the overlay washes the picture out; lower it to pick up cooler things. |
| | Overlay Intensity | Brightness of the heat that does show. Lower it if bodies read as solid white blobs rather than shapes. |
| | Full Mode Fill (%) | How bright Full mode fills a hot shape inside its outline. 100 is a solid shape; Outline mode ignores it. |
| | Magnify With Optic | Off by default. On draws the heat through a magnified scope's lens at the scope's zoom, at the cost of a second render while aiming. **Potential FPS improvement:** leave it off. |
| | Render Flashlight Heat | On by default. A lit flashlight's head shows faintly warm, from its lens back; lasers and infrared illuminators stay cold. Off, every flashlight reads cold. |
| | Render Searchlight Heat | On by default. A lit searchlight shows hot at its lens and warm around it; every other lamp stays cold. Off, searchlights read cold too. |
| | Sensor Refresh (Hz) | The thermal image updates at this rate and holds in between, as a real low-refresh core does. The frames between cost nothing. Default 60; 0 updates every frame. |
| **Controls** | Power Toggle | Click and press the combination you want. Default `Ctrl+N`. Keep a modifier - EFT does not demand an exact match on its own binds, so a bare `N` would toggle the goggles too. |
| | Mode Toggle | Switches Outline and Full. Default `Alt+N`. |
| | Thermal Mode | The mode the device powers up in. Mode Toggle changes it. |
| **Power Sequence** | Enabled | On by default. Off makes `Ctrl+N` switch the thermal instantly, with no messages and no click. |
| | Initializing / Warm-up Gap / Power Off Seconds | How long each stage lasts. Defaults 1.2, 0.3 and 1.5 - a little quicker than the real device. |
| | Click Volume | The calibration click, on top of the game's own volume. 0 mutes it. |
| **Debug** | Verbose Logging | Off for normal play. Writes detailed diagnostics to the BepInEx log if you are reporting a problem. |

Deliberately not exposed: the per-device mask geometry and the mount poses. Those are not preferences, they are measured values - exposing them mostly
offers a way to break the effect.

## Performance

The imager renders the scene a second time, off-screen, and composites the result inside the tube.
That pass runs only while the device is powered on and clipped to a host, and since 3.2.0 it is
built to cost as little as possible:

- **A heat-only shader.** The thermal camera draws each surface once, in a single unlit pass that
  writes its heat or black, instead of a full lit render plus the game's thermal post-processing.
- **Rendered at the sensor's rate.** The image updates at `Sensor Refresh` (60 Hz) and holds in
  between; at a high frame rate most frames render nothing.
- **Only the circle.** The camera renders just the part of the view inside the tube's circle.
- **One camera at a time.** While a magnified scope draws its own heat, the 1x camera rests.
- **Culled like your eyes.** The thermal skips what the main view would cull at the same distance.

Measured on SPT 4.1, same install and scene, three runs each (frame time, median):

| With the imager on | 3.1.0 | 3.2.0 |
|---|---|---|
| Cost at 1x, over the same view with it off | 0.41 ms | none measurable |
| Cost through a magnified scope, over the scope alone | 0.58 to 0.62 ms | 0.05 ms |

On that test that is about a quarter more frames per second while the imager is on. It was measured
at 400 to 500 fps on an empty test range with heat targets at 5 to 1000 m, which is the imager's
lightest case; a full map costs both versions more. The heat-only shader and the circle crop save the
same at any frame rate. The 60 Hz rendering saves more the higher your frame rate runs, since above
60 fps it skips more of the frames.

## Credits

**3D model by [3DMA - 3D Military Assets](https://www.3dmilitaryassets.com/)**, used under their
Extended Licence.

Thanks to Eukyre for pointing out that worn gear wants a dress script - that turned out to be
exactly how the device is hidden from the wearer's own view.

## Licence

The mod's own code is free to use. The 3D model is **not** - it is licensed from 3DMA and is
redistributed only in compiled form inside the asset bundle, as their licence permits. Do not
extract, redistribute, or reuse the model, its mesh or its textures.
