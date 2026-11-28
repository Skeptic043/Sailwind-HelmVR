using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    public enum TurnMode
    {
        Snap = 0,
        Smooth = 1
    }

    public enum GripMode
    {
        Hold = 0,
        ToggleBigItemsOnly = 1,
        ToggleAll = 2
    }

    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("Sailwind.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.skeptic043.sailwind.helmvr";
        public const string PluginName = "HelmVR";
        public const string Version = "0.11.0";
        private const string UuvrGuid = "raicuparta.uuvr-legacy";

        internal static ManualLogSource Log { get; private set; }

        // Controls
        internal static ConfigEntry<ControllerButton> PauseButtonConfig { get; private set; }
        internal static ConfigEntry<ControllerButton> BoatCameraButtonConfig { get; private set; }
        internal static ShortcutSetting BoatCameraKey { get; private set; }
        internal static ShortcutSetting RecenterKey { get; private set; }
        internal static ConfigEntry<bool> SwapFaceButtonsConfig { get; private set; }

        // Turning
        internal static ConfigEntry<TurnMode> TurnModeConfig { get; private set; }
        internal static ConfigEntry<float> SnapTurnAngleConfig { get; private set; }
        internal static ConfigEntry<float> SmoothTurnSpeedConfig { get; private set; }
        internal static ConfigEntry<float> TurnDeadzoneConfig { get; private set; }

        // Movement & Comfort
        internal static ConfigEntry<bool> HeadOrientedMovementConfig { get; private set; }
        internal static ConfigEntry<bool> EnableHeadLeaningConfig { get; private set; }
        internal static ConfigEntry<float> MaxLeanDistanceConfig { get; private set; }
        internal static ConfigEntry<float> BaseEyeHeightConfig { get; private set; }
        internal static ConfigEntry<float> HeightOffsetConfig { get; private set; }
        internal static ConfigEntry<bool> DisableHeadBobConfig { get; private set; }
        internal static ConfigEntry<bool> ComfortSleepHorizonConfig { get; private set; }
        internal static ConfigEntry<bool> OptimizeSleepRenderingConfig { get; private set; }
        internal static ConfigEntry<bool> ShowSleepParticlesConfig { get; private set; }
        internal static ConfigEntry<float> VignetteWhileMovingConfig { get; private set; }
        internal static ConfigEntry<float> VignetteWhileAboardConfig { get; private set; }
        internal static ConfigEntry<float> VignetteWhileStillConfig { get; private set; }

        // Controls & Motion Interaction
        internal static ConfigEntry<bool> EnableMotionControlsConfig { get; private set; }
        internal static ConfigEntry<GripMode> GripModeConfig { get; private set; }
        internal static ConfigEntry<float> NearGrabRadiusConfig { get; private set; }
        internal static ConfigEntry<float> RayFallbackDistanceConfig { get; private set; }
        internal static ConfigEntry<float> AimPitchAngleConfig { get; private set; }
        internal static ConfigEntry<float> ThrowVelocityMultiplierConfig { get; private set; }
        internal static ConfigEntry<float> RotarySensitivityConfig { get; private set; }
        internal static ConfigEntry<float> FishingReelSpeedConfig { get; private set; }
        internal static ConfigEntry<bool> InvertSteeringConfig { get; private set; }
        internal static ConfigEntry<float> ShipyardOrbitSpeedConfig { get; private set; }
        internal static ConfigEntry<bool> EnableVrChartingConfig { get; private set; }
        internal static ConfigEntry<bool> EnableHapticsConfig { get; private set; }
        internal static ConfigEntry<float> HapticStrengthConfig { get; private set; }

        // UI & Visuals
        internal static ConfigEntry<bool> HideFlatCrosshairConfig { get; private set; }
        internal static ConfigEntry<bool> EnableVrMenuPointerConfig { get; private set; }
        internal static ConfigEntry<bool> DisableOcclusionCullingConfig { get; private set; }
        internal static ConfigEntry<bool> ShowHandVisualsConfig { get; private set; }
        internal static ConfigEntry<float> HandVerticalOffsetConfig { get; private set; }
        internal static ConfigEntry<bool> ShowGameplayAimRayConfig { get; private set; }
        internal static ConfigEntry<bool> ShowHoverTooltipsConfig { get; private set; }
        internal static ConfigEntry<bool> ShowVrActionPromptsConfig { get; private set; }
        internal static ConfigEntry<float> NeedsUiDistanceConfig { get; private set; }
        internal static ConfigEntry<float> NeedsUiHeightOffsetConfig { get; private set; }
        internal static ConfigEntry<float> NeedsUiTiltAngleConfig { get; private set; }

        // Diagnostics
        internal static ConfigEntry<bool> LogButtonEventsConfig { get; private set; }
        internal static ShortcutSetting DiagnosticsKey { get; private set; }

        private void Awake()
        {
            Log = Logger;
            SeedUuvrHorizonDefault();

            const string controls = "1. Controls";
            PauseButtonConfig = Config.Bind(controls, "PauseButton", ControllerButton.Y,
                "Controller button that opens and closes the pause menu. The recessed left menu button is kept by SteamVR on Quest controllers.");
            BoatCameraButtonConfig = Config.Bind(controls, "BoatCameraButton", ControllerButton.None,
                "Controller button that toggles Sailwind's third-person boat camera. Leave as None to avoid accidental toggles on your controllers.");
            BoatCameraKey = new ShortcutSetting(Config, controls, "BoatCameraKey",
                "Keyboard shortcut that toggles the third-person boat camera.", "C");
            RecenterKey = new ShortcutSetting(Config, controls, "RecenterKey",
                "Keyboard shortcut that recenters your view and menus, like clicking both sticks.", "F9");
            SwapFaceButtonsConfig = Config.Bind(controls, "SwapFaceButtons", false,
                "Swap the bottom (A/X) and top (B/Y) face buttons. Turn on if your controller reports them the other way round, for example if the bottom left button opens the pause menu instead of the inventory.");

            TurnModeConfig = Config.Bind(
                "2. Turning",
                "TurnMode",
                TurnMode.Snap,
                "Choose between Snap turning and Smooth turning on the right thumbstick.");

            SnapTurnAngleConfig = Config.Bind(
                "2. Turning",
                "SnapTurnAngle",
                45f,
                new ConfigDescription(
                    "Degrees rotated per snap turn.",
                    new AcceptableValueRange<float>(15f, 90f)));

            SmoothTurnSpeedConfig = Config.Bind(
                "2. Turning",
                "SmoothTurnSpeed",
                200f,
                new ConfigDescription(
                    "Degrees per second for smooth turning at full thumbstick deflection.",
                    new AcceptableValueRange<float>(30f, 500f)));

            TurnDeadzoneConfig = Config.Bind(
                "2. Turning",
                "TurnDeadzone",
                0.5f,
                new ConfigDescription(
                    "Thumbstick X threshold for snap turn (smooth turn uses a 0.2 deadzone).",
                    new AcceptableValueRange<float>(0.2f, 0.9f)));

            HeadOrientedMovementConfig = Config.Bind(
                "3. Movement and Comfort",
                "HeadOrientedMovement",
                true,
                "Move in the direction your headset is facing when pushing the left thumbstick.");

            EnableHeadLeaningConfig = Config.Bind(
                "3. Movement and Comfort",
                "EnableHeadLeaning",
                true,
                "Allow 6DoF head leaning independent of the character body (so leaning over the gunwale does not drag the boat with you).");

            MaxLeanDistanceConfig = Config.Bind(
                "3. Movement and Comfort",
                "MaxLeanDistance",
                0.75f,
                new ConfigDescription(
                    "Maximum horizontal head-lean distance in meters from your body anchor (0 = unlimited).",
                    new AcceptableValueRange<float>(0f, 2.0f)));

            BaseEyeHeightConfig = Config.Bind(
                "3. Movement and Comfort",
                "BaseEyeHeight",
                0.97f,
                new ConfigDescription(
                    "Base OVRCameraRig local Y height under UUVR RelativeMatrix mode (Sailwind default is 1.1).",
                    new AcceptableValueRange<float>(-0.8f, 1.8f)));

            HeightOffsetConfig = Config.Bind(
                "3. Movement and Comfort",
                "HeightOffset",
                0f,
                new ConfigDescription(
                    "Fine vertical eye-height adjustment in meters.",
                    new AcceptableValueRange<float>(-1.0f, 1.0f)));

            DisableHeadBobConfig = Config.Bind(
                "3. Movement and Comfort",
                "DisableHeadBob",
                true,
                "Disable Sailwind's HeadBob script in VR so it does not fight headset height or cause nausea.");

            ComfortSleepHorizonConfig = Config.Bind(
                "3. Movement and Comfort",
                "ComfortSleepHorizon",
                true,
                "Keep the VR horizon level when lying down in a bed.");

            OptimizeSleepRenderingConfig = Config.Bind(
                "3. Movement and Comfort",
                "OptimizeSleepRendering",
                true,
                "Skip 3D world rendering while your eyes are fully closed during sleep to improve VR performance at 16x speed (keeps the sleep bar visible).");

            ShowSleepParticlesConfig = Config.Bind(
                "3. Movement and Comfort",
                "ShowSleepParticles",
                true,
                "Show floating sleep particles during sleep blackout. Turn off to save extra performance during 16x sleep.");

            VignetteWhileMovingConfig = Config.Bind(
                "3. Movement and Comfort",
                "VignetteWhileMoving",
                0f,
                new ConfigDescription(
                    "Peripheral comfort vignette strength while walking, swimming, or smooth-turning (0 = off).",
                    new AcceptableValueRange<float>(0f, 1f)));

            VignetteWhileAboardConfig = Config.Bind(
                "3. Movement and Comfort",
                "VignetteWhileAboard",
                0f,
                new ConfigDescription(
                    "Peripheral comfort vignette strength whenever you are aboard a ship (0 = off).",
                    new AcceptableValueRange<float>(0f, 1f)));

            VignetteWhileStillConfig = Config.Bind(
                "3. Movement and Comfort",
                "VignetteWhileStill",
                0f,
                new ConfigDescription(
                    "Peripheral comfort vignette strength while standing still anywhere (0 = off).",
                    new AcceptableValueRange<float>(0f, 1f)));

            EnableMotionControlsConfig = Config.Bind(
                "4. Hands",
                "EnableMotionControls",
                true,
                "Enable dual-hand VR grabbing, carrying, throwing, and physical winch/wheel/pump controls.");

            GripModeConfig = Config.Bind(
                "4. Hands",
                "GripMode",
                GripMode.ToggleAll,
                "ToggleAll: press Grip once to hold and again to drop/throw. ToggleBigItemsOnly: toggle Grip for heavy/big cargo while holding Grip for small items. Hold: hold Grip continuously.");

            NearGrabRadiusConfig = Config.Bind(
                "4. Hands",
                "NearGrabRadius",
                0.18f,
                new ConfigDescription(
                    "Near-hand proximity grab radius in meters.",
                    new AcceptableValueRange<float>(0.08f, 0.35f)));

            RayFallbackDistanceConfig = Config.Bind(
                "4. Hands",
                "RayFallbackDistance",
                2.0f,
                new ConfigDescription(
                    "Controller ray distance in meters when nothing is within near-hand reach.",
                    new AcceptableValueRange<float>(0.3f, 3.0f)));

            AimPitchAngleConfig = Config.Bind(
                "4. Hands",
                "AimPitchAngle",
                40f,
                new ConfigDescription(
                    "Downward pitch offset in degrees for controller aim rays (40 degrees matches natural Quest 3 wrist angle).",
                    new AcceptableValueRange<float>(-20f, 75f)));

            ThrowVelocityMultiplierConfig = Config.Bind(
                "4. Hands",
                "ThrowVelocityMultiplier",
                1.35f,
                new ConfigDescription(
                    "Velocity multiplier when releasing a moving item to throw it.",
                    new AcceptableValueRange<float>(0.5f, 3.0f)));

            RotarySensitivityConfig = Config.Bind(
                "4. Hands",
                "RotarySensitivity",
                1.2f,
                new ConfigDescription(
                    "Hand rotation sensitivity for physical winches, steering wheels, and bilge pumps.",
                    new AcceptableValueRange<float>(0.4f, 3.0f)));

            FishingReelSpeedConfig = Config.Bind(
                "4. Hands",
                "FishingReelSpeed",
                120f,
                new ConfigDescription(
                    "Reeling speed multiplier when pushing Right Thumbstick Up/Down while holding a fishing rod.",
                    new AcceptableValueRange<float>(20f, 300f)));

            InvertSteeringConfig = Config.Bind(
                "4. Hands",
                "InvertSteering",
                false,
                "Invert left/right steering direction when gripping a helm or tiller.");

            ShipyardOrbitSpeedConfig = Config.Bind(
                "4. Hands",
                "ShipyardOrbitSpeed",
                90f,
                new ConfigDescription(
                    "Degrees per second for orbiting the boat camera with the right thumbstick in the Shipyard.",
                    new AcceptableValueRange<float>(30f, 220f)));

            EnableVrChartingConfig = Config.Bind(
                "4. Hands",
                "EnableVrCharting",
                true,
                "Use 1:1 VR tabletop charting (keep stereo VR head view and aim the laser directly at the chart and charting kit).");

            EnableHapticsConfig = Config.Bind(
                "4. Hands",
                "EnableHaptics",
                true,
                "Send subtle haptic pulses on hover, grab, release, and winch/wheel rotation.");

            HapticStrengthConfig = Config.Bind(
                "4. Hands",
                "HapticStrength",
                0.45f,
                new ConfigDescription(
                    "Overall haptic vibration strength.",
                    new AcceptableValueRange<float>(0.05f, 1.0f)));

            HideFlatCrosshairConfig = Config.Bind(
                "5. UI and Visuals",
                "HideFlatCrosshair",
                true,
                "Hide Sailwind's flat-screen 7 cm dot crosshair in VR.");

            EnableVrMenuPointerConfig = Config.Bind(
                "5. UI and Visuals",
                "EnableVrMenuPointer",
                true,
                "Use VR controller laser rays and trigger to aim and click Sailwind's 3D menus.");

            DisableOcclusionCullingConfig = Config.Bind(
                "5. UI and Visuals",
                "DisableOcclusionCulling",
                true,
                "Disable camera occlusion culling in VR to reduce right-eye-only island flicker under Multi-Pass stereo.");

            ShowHandVisualsConfig = Config.Bind(
                "5. UI and Visuals",
                "ShowHandVisuals",
                true,
                "Show VR hand meshes and aim rays.");

            HandVerticalOffsetConfig = Config.Bind(
                "5. UI and Visuals",
                "HandVerticalOffset",
                0f,
                new ConfigDescription(
                    "Vertical offset in meters for the visual native VR hand meshes.",
                    new AcceptableValueRange<float>(-0.45f, 0.25f)));

            ShowGameplayAimRayConfig = Config.Bind(
                "5. UI and Visuals",
                "ShowGameplayAimRay",
                true,
                "Show a thin pointer beam during gameplay when aiming at a distant interactable (menu lasers always show in menus).");

            ShowHoverTooltipsConfig = Config.Bind(
                "5. UI and Visuals",
                "ShowHoverTooltips",
                true,
                "Show Sailwind's floating world-space LookUI hover text (item names, prices, prompts) for VR hands.");

            ShowVrActionPromptsConfig = Config.Bind(
                "5. UI and Visuals",
                "ShowVrActionPrompts",
                true,
                "Show short [Grip] / [Trig] button labels on LookUI tooltips in place of desktop mouse icons.");

            NeedsUiDistanceConfig = Config.Bind(
                "5. UI and Visuals",
                "NeedsUiDistance",
                0.78f,
                new ConfigDescription(
                    "Forward distance in meters for the Inventory, Needs, and Logbook panels in VR.",
                    new AcceptableValueRange<float>(0.4f, 1.3f)));

            NeedsUiHeightOffsetConfig = Config.Bind(
                "5. UI and Visuals",
                "NeedsUiHeightOffset",
                -0.12f,
                new ConfigDescription(
                    "Vertical offset in meters for the Inventory and Logbook panels in VR.",
                    new AcceptableValueRange<float>(-0.5f, 0.3f)));

            NeedsUiTiltAngleConfig = Config.Bind(
                "5. UI and Visuals",
                "NeedsUiTiltAngle",
                -18f,
                new ConfigDescription(
                    "Tilt angle in degrees for the Inventory and Logbook panels (negative tilts the bottom toward you like a clipboard).",
                    new AcceptableValueRange<float>(-45f, 30f)));

            LogButtonEventsConfig = Config.Bind(
                "6. Diagnostics",
                "LogButtonEvents",
                false,
                "Log VR controller button press/release transitions to LogOutput.log.");

            DiagnosticsKey = new ShortcutSetting(Config, "6. Diagnostics", "DiagnosticsKey",
                "Keyboard shortcut that writes XR, camera, height and controller details to LogOutput.log.");

            var harmony = new Harmony(Guid);
            Type[] patchTypes =
            {
                typeof(VrInputPatches),
                typeof(MenuPointerPatches),
                typeof(VrHandPatches),
                typeof(VrRotaryPatches),
                typeof(VrLookUiPatches),
                typeof(VrShipyardPatches),
                typeof(VrChartingPatches)
            };

            foreach (Type patchType in patchTypes)
            {
                try
                {
                    harmony.PatchAll(patchType);
                }
                catch (Exception e)
                {
                    Log.LogWarning("Could not apply " + patchType.Name + ": " + e.GetBaseException().Message);
                }
            }

            // The controller lives on its own object, and every scene load checks that it still exists. Sailwind can
            // destroy BepInEx's shared manager object during boot, which would otherwise leave HelmVR dead for the session.
            EnsureRigController();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => EnsureRigController();
            Log.LogInfo(PluginName + " " + Version + " loaded.");
        }

        private static bool _rigCreated;

        private static void EnsureRigController()
        {
            if (VrRigController.Instance != null) return;
            if (_rigCreated)
            {
                Log.LogWarning("HelmVR's controller object was destroyed; recreating it.");
            }
            var host = new GameObject("HelmVR");
            DontDestroyOnLoad(host);
            host.AddComponent<VrRigController>();
            _rigCreated = true;
        }

        // UUVR defaults to letting the game camera's pitch and roll into the headset view, which tilts Sailwind's menus.
        // Seed UUVR's settings file with Align To Horizon on, only when the file doesn't exist yet, so a player's later
        // choice is never overridden. HelmVR binds before UUVR (plugins load in GUID order), so UUVR reads this file.
        private static void SeedUuvrHorizonDefault()
        {
            try
            {
                string path = System.IO.Path.Combine(Paths.ConfigPath, UuvrGuid + ".cfg");
                if (System.IO.File.Exists(path)) return;
                System.IO.File.WriteAllText(path, "[Camera]\n\nAlign To Horizon = true\n");
                Log.LogInfo("Created UUVR settings with Align To Horizon on.");
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not seed UUVR's Align To Horizon setting: " + e.Message);
            }
        }
    }
}
