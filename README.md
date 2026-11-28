# Sailwind HelmVR

Play Sailwind in VR. Grab and throw cargo, use winches, take the wheel and plot courses on the chart table.

## Requirements

- A PC VR headset that runs SteamVR games, with SteamVR installed
- [UUVR](https://github.com/Raicuparta/uuvr) (installed automatically when adding HelmVR in Mod Sync, or via Rai Pal)

HelmVR builds on UUVR and requires it to run.

## Install

### Sailwind Mod Synchronizer (recommended)

Install HelmVR through [Sailwind Mod Synchronizer](https://github.com/foxyv/SailwindModSynchronizer/releases/latest) by adding it to a ModPack. Mod Sync offers to install UUVR alongside it and sets up BepInEx 5 automatically.

### Rai Pal (alternative)

1. Open [Rai Pal](https://github.com/Raicuparta/rai-pal/releases/latest), select Sailwind and install `UUVR` and optionally `Config Manager`. Rai Pal installs BepInEx 5 along with them.
2. Download and extract the [HelmVR ZIP](https://github.com/Skeptic043/Sailwind-HelmVR/releases/latest). Copy its `BepInEx/plugins/HelmVR` folder into Rai Pal's Sailwind plugins folder: `%APPDATA%\raicuparta\rai-pal\data\installed-mods\<number>\bepinex\BepInEx\plugins`. `<number>` is Rai Pal's ID for your Sailwind install, and there is usually only one.

## Launch

Before launching, set the SteamVR advanced setting `Present Non-VR Applications on Theater Screen Upon Launch` to off. This prevents a SteamVR interface from getting stuck on screen when the game boots.

Start SteamVR with your headset connected, then launch your VR pack with `Play` in Mod Sync (or from Steam/Rai Pal if you installed UUVR with Rai Pal). The first loading screen is usually upside down, which is harmless.

### Switching between VR and flatscreen

**With Mod Sync:** Launching a pack without UUVR, turning UUVR off in your pack, or pressing `Launch Vanilla` turns UUVR's VR setting back off so Sailwind opens on your monitor. Launching a pack with UUVR turns VR back on when the game starts.

**With Rai Pal:** UUVR leaves VR turned on in Sailwind's game files after you close the game. If Sailwind opens in your headset when you want to play on your monitor, run `Verify integrity of game files` in Sailwind's Steam properties. Rai Pal and Mod Sync also share `doorstop_config.ini` in your Sailwind folder, so if you switch between them, launching from Mod Sync points Steam at Mod Sync until you reinstall UUVR in Rai Pal.

## Controls

Button names are for Quest Touch controllers. If `A` and `X` act like `B` and `Y` on your controller, turn on `SwapFaceButtons` in [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager).

| Action | Control |
| --- | --- |
| Move | Left stick, in the direction you're looking |
| Turn | Right stick left or right |
| Grab, carry, drop or throw | `Grip` |
| Use, click or interact | `Trigger` |
| Scroll (reel in, spyglass, knobs) | Right stick up or down |
| Jump | `A` |
| Inventory | `X` |
| Pause menu | `Y` |
| Show the pointer laser | Hold `B` |
| Sprint on or off | Click the left stick |
| Crouch on or off | Click the right stick |
| Recenter view and menus | Click both sticks |

Your view also recenters by itself once the main menu has loaded and again once your save has loaded.

Grip a winch and use up and down on the joystick to raise/lower. Grip a wheel or tiller, then move your hand sideways or push the stick to steer, and pull `Trigger` to lock or unlock it. Grip an inventory or crate slot to take an item out, and let go of an item over a slot to put it in. In the Shipyard, the right stick orbits the boat and the left stick zooms and pans. At the chart table, aim at the chart and charting tools with your laser, and press `B` or `Y` to step away.

## Configuration

Settings are in `BepInEx/config/com.skeptic043.sailwind.helmvr.cfg` after the first launch, or in game with Sinai's [BepInExConfigManager (Mono)](https://github.com/sinai-dev/BepInExConfigManager/releases) (`BepInExConfigManager.Mono.zip`, opened with `F5`). Use Sinai's Mono version in VR rather than standard BepInEx Configuration Manager (`F1`), because standard IMGUI menus only draw on your desktop monitor and do not appear inside the headset. Changes apply immediately. Below are the commonly changed settings.

| Setting | Default | Options |
| --- | --- | --- |
| `TurnMode` | `Snap` | `Snap` or `Smooth` turning on the right stick. |
| `SnapTurnAngle` | `45` | Degrees per snap turn, 15 to 90. |
| `SmoothTurnSpeed` | `200` | Degrees per second for smooth turning, 30 to 500. |
| `HeadOrientedMovement` | `true` | Move where you're looking. Turn off to move where your body faces. |
| `EnableHeadLeaning` | `true` | Allow 6DoF head leaning independent of your body so leaning over the gunwale does not drag the boat with you. |
| `MaxLeanDistance` | `0.75` | Maximum horizontal head-lean distance in meters from your body (`0` = unlimited). |
| `HeightOffset` | `0` | Raises or lowers your eye height in meters, -1 to 1. |
| `VignetteWhileMoving` | `0` | Peripheral comfort vignette strength while moving or smooth-turning, `0` to `1` (`0` = off). |
| `VignetteWhileAboard` | `0` | Peripheral comfort vignette strength whenever you are aboard a ship, `0` to `1` (`0` = off). |
| `VignetteWhileStill` | `0` | Peripheral comfort vignette strength while standing still anywhere, `0` to `1` (`0` = off). |
| `GripMode` | `ToggleAll` | `ToggleAll` (press `Grip` once to hold and again to let go), `ToggleBigItemsOnly` (toggle only for big cargo), or `Hold` (keep holding while `Grip` is down). |
| `InvertSteering` | `false` | Reverses steering direction on wheels and tillers. |
| `PauseButton` | `Y` | Controller button that opens the pause menu. |
| `BoatCameraButton` | `None` | Controller button that toggles the third-person boat camera (leave `None` to avoid accidental controller toggles). |
| `BoatCameraKey` | `C` | Keyboard shortcut that toggles the third-person boat camera. |
| `RecenterKey` | `F9` | Keyboard shortcut that recenters your view, like clicking both sticks. |
| `EnableHaptics` | `true` | Controller vibration on grabs, winches and the wheel. `HapticStrength` sets how strong. |
| `ShowHoverTooltips` | `true` | Floating item names and prices when you point at things. |
| `ComfortSleepHorizon` | `true` | Keeps the horizon level when you lie down in a bed. |
| `OptimizeSleepRendering` | `true` | Skips 3D world rendering while your eyes are closed during 16x sleep (keeps the sleep bar visible). |
| `ShowSleepParticles` | `true` | Shows floating sleep particles during sleep blackout. |

Keyboard shortcuts such as `BoatCameraKey` and `RecenterKey` accept any key name such as `F7`, add modifiers with `+` as in `LeftShift + F7`, or can be left blank to turn the shortcut off.

## Compatibility

No known incompatibilities. HelmVR includes built-in VR control support for these mods when installed:

- [Dizzy Firewood Bundle](https://github.com/foxyv/DizzyFirewoodBundle) (Bundle Up): pull `Trigger` while holding food, firewood, or hooks aimed at a matching piece to bundle them, or pull `Trigger` empty-handed on loose items in a crate or on deck to gather them
- [Dizzy Clothesline](https://github.com/foxyv/DizzyClothesline): pull `Trigger` empty-handed on two empty lamp hooks to tie a line, and release `Grip` while aiming along the line to hang items at your aim point
- [Dizzy Nudge](https://github.com/foxyv/HammerNudge): while holding a hammer aimed at a nudgeable item, hold your off-hand `Trigger` and use the left stick (horizontal) and right stick up/down (vertical) to nudge, hold off-hand `Grip` to rotate, and tap `A` or click the right stick to level

HelmVR has also been tested alongside these mods:

- [Hooks Hang More](https://github.com/bryon82/SailwindHooksHangMore)
- [MoreSailwindSails](https://github.com/sum-rock/MoreSailwindSails)
- [Sail-a-dex](https://github.com/bryon82/SailwindSail-a-dex)
- [Scrambled Seas](https://github.com/NANDbrew/scrambled-seas)
- [Sailwind Difficulty](https://github.com/NANDbrew/SailwindDifficulty)
- [ShipShape](https://github.com/NANDbrew/ShipShape)
- [Voyage Recorder](https://github.com/Skeptic043/sailwind-voyage-recorder)
- [Windicators](https://github.com/NANDbrew/Windicators)
- [Shipyard Expansion](https://github.com/NANDbrew/ShipyardExpansion)
- [Dizzy Sailwind Fixes](https://github.com/foxyv/dizzy_sailwind_fixes)
- [Keevi Fixes](https://github.com/KeeviDev/SailwindKeeviFixes)
- [NAND Fixes](https://github.com/NANDbrew/NANDFixes)
- [RadFixes](https://github.com/bryon82/SailwindRadFixes)
- [Performance Tweaks](https://github.com/KeeviDev/SailwindPerformanceTweaks)
- [Sailwind Radio](https://github.com/Skeptic043/Sailwind-Radio)

## Known issues

- The first loading screen before the main menu is usually upside down
- The spyglass extends and retracts, but the view through it doesn't zoom in
- Faint white vertical lines can show in the view in some locations
- Some flickering occurs in one eye when looking at certain pieces of land
- Some shading and shadows can show in one eye but not the other
- With Rai Pal, a Sailwind update can cause a crash while loading with UUVR. Delete `globalgamemanagers` and `globalgamemanagers.bak` from the `Sailwind_Data` folder, then run `Verify integrity of game files` in Steam (Mod Sync clears stale UUVR backups automatically on launch)

## Credits

- HelmVR is built on [UUVR](https://github.com/Raicuparta/uuvr) by Raicuparta, which is the foundation this mod stands on. 
- Raw Lion Workshop built a large part of the VR base before HelmVR existed: Sailwind still carries its original VR camera rig and Touch controller pointers, and HelmVR brings those back to life.

## AI Use

AI was used to write all of the code in this project. The original concept, design direction, testing, debugging, and release decisions are my own. If you prefer not to use mods developed with AI assistance, I understand and respect that choice.

## Links

[Report an issue](https://github.com/Skeptic043/Sailwind-HelmVR/issues) · [Source code](https://github.com/Skeptic043/Sailwind-HelmVR) · [Release notes](https://github.com/Skeptic043/Sailwind-HelmVR/blob/main/CHANGELOG.md) · [Build instructions](https://github.com/Skeptic043/Sailwind-HelmVR/blob/main/BUILDING.md) · [License](https://github.com/Skeptic043/Sailwind-HelmVR/blob/main/LICENSE) · [Support on Ko-fi](https://ko-fi.com/skeptic043) · skeptic043
