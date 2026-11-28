# Changelog

## 0.11.0

### Added
- 6DoF head leaning independent of the character body with configurable `EnableHeadLeaning` and `MaxLeanDistance`, plus leaning in on the Inventory and Logbook (thanks Dezibahn on Discord for the suggestion)
- Sleep rendering optimization (`OptimizeSleepRendering`) and sleep particles toggle (`ShowSleepParticles`) for smoother 16x sleep in VR (thanks Dezibahn on Discord for the suggestion)
- Peripheral comfort vignette options for moving/turning (`VignetteWhileMoving`), aboard a ship (`VignetteWhileAboard`), and standing still (`VignetteWhileStill`)
- VR support for [Dizzy Firewood Bundle](https://github.com/foxyv/DizzyFirewoodBundle) (bundling held food/firewood/hooks and gathering loose items in crates or on deck can be done by pressing `Trigger` while aiming the item), [Dizzy Clothesline](https://github.com/foxyv/DizzyClothesline) (tying lines can be started/finished on empty hooks with `Trigger`), and [Dizzy Nudge](https://github.com/foxyv/HammerNudge) (hold off-hand `Trigger` while holding a hammer to start nudging: use left stick for the x axis, right stick for the y axis, and hold `Grip` with your off hand for rotation controls; leveling can be done with `A` or right stick click)

### Changed
- General items and cargo now stay grabbed in place like vanilla instead of warping to your hand, while most hand-held tools still snap into your palm
- Releasing `Grip` while aiming a hangable item at a lamp hook hangs it on the hook
- Pulling `Trigger` with a hammer now nails/un-nails hooks (including with [Hooks Hang More](https://github.com/bryon82/SailwindHooksHangMore))
- Releasing a winch now plays the same sound as grabbing one
- `BoatCameraKey` now defaults to `C` like in the flatscreen game and `RecenterKey` defaults to `F9` (recentering can still be done by clicking both sticks, and Boat Cam can be bound to a VR controller key in the config)
- Pulling `Trigger` while not aimed at anything now closes open crate and inventory panels

### Fixed
- Attempted to better sync VR hand raycasts to run after boat movement with a thin sphere-cast assist and brief hover-drop grace, fixing flicker on small buttons and item edges while underway
- Restored VR thumbstick orbit, pitch, and zoom controls when viewing the boat in `C` camera outside the Shipyard
- Fixed hammers not being placeable on tables, shelves, and crate lids
- Stabilized the sleep bar and sleep particles during sleep
- Fixed moored rope length adjustment being extremely slow in VR
- Fixed hold-repeat Shipyard buttons (such as sail position and size) staying stuck after releasing `Trigger`

## 0.10.4

### Initial Release
- Play Sailwind in VR with help from [UUVR](https://github.com/Raicuparta/uuvr)
- Grab, carry, throw items, use tools, and work winches, wheels, tillers and bilge pumps using VR controllers
- Full shipyard support
- Snap or smooth turning, laser menu pointers and comfort options
