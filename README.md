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

Start SteamVR with your headset connected, then launch your VR pack with `Play` in Mod Sync (or from Steam/Rai Pal if you installed UUVR with Rai Pal). The first loading screen is usually upside down, which is harmless.

### Switching between VR and flatscreen

When you use Mod Sync, launching a pack without UUVR, turning UUVR off in your pack, or pressing `Launch Vanilla` turns UUVR's VR setting back off so Sailwind opens on your monitor. Launching a pack with UUVR turns VR back on when the game starts.

If you installed through Rai Pal instead, UUVR leaves VR turned on in Sailwind's game files after you close the game. If Sailwind opens in your headset when you want to play on your monitor, run `Verify integrity of game files` in Sailwind's Steam properties. Rai Pal and Mod Sync also share `doorstop_config.ini` in your Sailwind folder, so if you switch between them, launching from Mod Sync points Steam at Mod Sync until you reinstall UUVR in Rai Pal.

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

Grip a winch and use up and down on the joystick to raise/lower. Grip a wheel or tiller, then move your hand sideways or push the stick to steer, and pull `Trigger` to lock or unlock it. Grip an inventory or crate slot to take an item out, and let go of an item over a slot to put it in. Hold an item over a table, shelf or crate lid and it sits on the surface, then let go to set it down there. In the Shipyard, the right stick orbits the boat and the left stick zooms and pans. At the chart table, aim at the chart and charting tools with your laser, and press `B` or `Y` to step away.

## Configuration

Settings are in `BepInEx/config/com.skeptic043.sailwind.helmvr.cfg` after the first launch, or in game with [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager) (`F1`, or `F5` in Rai Pal's Config Manager). Changes apply immediately. Below are the commonly changed settings. 

| Setting | Default | Options |
| --- | --- | --- |
| `TurnMode` | `Snap` | `Snap` or `Smooth` turning on the right stick. |
| `SnapTurnAngle` | `45` | Degrees per snap turn, 15 to 90. |
| `SmoothTurnSpeed` | `200` | Degrees per second for smooth turning, 30 to 500. |
| `HeadOrientedMovement` | `true` | Move where you're looking. Turn off to move where your body faces. |
| `HeightOffset` | `0` | Raises or lowers your eye height in meters, -1 to 1. |
| `GripMode` | `ToggleAll` | `ToggleAll` (press `Grip` once to hold and again to let go), `ToggleBigItemsOnly` (toggle only for big cargo), or `Hold` (keep holding while `Grip` is down). |
| `InvertSteering` | `false` | Reverses steering direction on wheels and tillers. |
| `PauseButton` | `Y` | Controller button that opens the pause menu. |
| `BoatCameraButton` | `None` | Controller button that toggles the third-person boat camera, which stays off in VR until you bind this or `BoatCameraKey`. |
| `RecenterKey` | blank | Keyboard shortcut that recenters your view, like clicking both sticks. |
| `EnableHaptics` | `true` | Controller vibration on grabs, winches and the wheel. `HapticStrength` sets how strong. |
| `ShowHoverTooltips` | `true` | Floating item names and prices when you point at things. |
| `ComfortSleepHorizon` | `true` | Keeps the horizon level when you lie down in a bed. |

Keyboard shortcuts such as `BoatCameraKey` and `RecenterKey` are blank by default. Type a key name such as `F7`, add modifiers with `+` as in `LeftShift + F7`, or leave the value blank to turn the shortcut off.

## Compatibility

No known incompatibilities. HelmVR has been tested alongside these mods:

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
