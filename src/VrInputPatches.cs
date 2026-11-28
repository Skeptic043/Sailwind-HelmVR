using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    /// <summary>
    /// Bridges VR controller inputs into Sailwind's GameInput cleanly without legacy JoystickButton bleed-through,
    /// provides toggle Sprint and toggle Crouch, blocks accidental BoatCamera toggles,
    /// keeps Sleep beds horizon-level, and hides Sailwind's parked Hints scroll banner when idle.
    /// </summary>
    internal static class VrInputPatches
    {
        private const float StickDeadzone = 0.12f;

        private static bool _sprintToggled;
        private static readonly FieldInfo HintsTextField = AccessTools.Field(typeof(Hints), "hintText");
        private static readonly FieldInfo HintsTimerField = AccessTools.Field(typeof(Hints), "hintTimer");

        private static bool IsActiveVr()
        {
            VrRigController rig = VrRigController.Instance;
            return rig != null && rig.IsVrActive;
        }

        internal static void UpdateToggleStates()
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive)
            {
                _sprintToggled = false;
                return;
            }

            // Toggle sprint on Left Stick Click (works both on foot and while swimming); auto-clear when stopping or entering menus/bed.
            if (rig.LeftHand.StickClickDown)
            {
                _sprintToggled = !_sprintToggled;
            }

            if (_sprintToggled)
            {
                bool isMoving = rig.LeftHand.Stick.magnitude >= StickDeadzone;
                bool grippingWinch = rig.HandInteractor.IsAnyHandGrippingWinch();
                if ((!isMoving && !grippingWinch) || GameState.inCursorMenu || GameState.inBed != null)
                {
                    _sprintToggled = false;
                }
            }
        }

        private static bool IsKeyboardKey(InputName inputName, int mode)
        {
            KeyCode k1 = GameInput.GetKeyCode(inputName, false, false);
            KeyCode k2 = GameInput.GetKeyCode(inputName, true, false);
            if (mode == 0) return (k1 != KeyCode.None && Input.GetKey(k1)) || (k2 != KeyCode.None && Input.GetKey(k2));
            if (mode == 1) return (k1 != KeyCode.None && Input.GetKeyDown(k1)) || (k2 != KeyCode.None && Input.GetKeyDown(k2));
            return (k1 != KeyCode.None && Input.GetKeyUp(k1)) || (k2 != KeyCode.None && Input.GetKeyUp(k2));
        }

        private static bool IsBoatCameraInput(VrRigController rig, int mode)
        {
            // Keep Sailwind's native CameraMode keyboard key (C by default) working alongside any custom key or VR button.
            return IsKeyboardKey(InputName.CameraMode, mode)
                || Plugin.BoatCameraKey.Check(mode)
                || ControllerButtons.Check(Plugin.BoatCameraButtonConfig.Value, rig.LeftHand, rig.RightHand, mode);
        }

        private static Vector2 GetOrientedMoveStick()
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive) return Vector2.zero;

            // Freeze locomotion while gripping a winch, helm/tiller, bilge pump, or actively nudging with Dizzy.Nudge.
            if (rig.HandInteractor.IsAnyHandGrippingAnyRotary() || VrDizzyCompat.IsVrNudgeActive) return Vector2.zero;

            Vector2 raw = rig.LeftHand.Stick;
            if (raw.magnitude < StickDeadzone) return Vector2.zero;

            if (!Plugin.HeadOrientedMovementConfig.Value
                || !GameState.playing
                || GameState.inCursorMenu
                || Refs.observerMirror == null)
            {
                return raw;
            }

            Transform observer = Refs.observerMirror.transform;
            Vector3 up = observer.up;
            Vector3 bodyFwd = Vector3.ProjectOnPlane(observer.forward, up).normalized;
            Vector3 eyeFwd = Vector3.ProjectOnPlane(rig.EyeWorldRotation * Vector3.forward, up).normalized;
            if (bodyFwd.sqrMagnitude < 0.001f || eyeFwd.sqrMagnitude < 0.001f)
            {
                return raw;
            }

            float angleDeg = Vector3.SignedAngle(bodyFwd, eyeFwd, up);
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            float outX = raw.x * cos + raw.y * sin;
            float outY = -raw.x * sin + raw.y * cos;
            return new Vector2(outX, outY);
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetPrimaryHorizontal))]
        [HarmonyPrefix]
        private static bool GetPrimaryHorizontalPrefix(ref float __result)
        {
            if (!IsActiveVr()) return true;
            __result = GetOrientedMoveStick().x;
            return false;
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetPrimaryVertical))]
        [HarmonyPrefix]
        private static bool GetPrimaryVerticalPrefix(ref float __result)
        {
            if (!IsActiveVr()) return true;
            __result = GetOrientedMoveStick().y;
            return false;
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetSecondaryHorizontal))]
        [HarmonyPrefix]
        private static bool GetSecondaryHorizontalPrefix(ref float __result)
        {
            if (!IsActiveVr()) return true;
            __result = 0f;
            return false;
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetSecondaryVertical))]
        [HarmonyPrefix]
        private static bool GetSecondaryVerticalPrefix(ref float __result)
        {
            if (!IsActiveVr()) return true;
            __result = 0f;
            return false;
        }

        // Prevent Sailwind's legacy Oculus trigger-to-scroll mapping from zooming Shipyard/BoatCamera on Trigger press,
        // while routing Right Thumbstick Y into GetScrollAxis during normal gameplay so modded scroll controls (e.g. Radio volume) work.
        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetScrollAxis))]
        [HarmonyPrefix]
        private static bool GetScrollAxisPrefix(ref float __result)
        {
            if (!IsActiveVr()) return true;
            if (IsKeyboardKey(InputName.ScrollUp, 0))
            {
                __result = Time.deltaTime * 4.6f;
                return false;
            }
            if (IsKeyboardKey(InputName.ScrollDown, 0))
            {
                __result = -Time.deltaTime * 4.6f;
                return false;
            }

            VrRigController rig = VrRigController.Instance;
            if (rig != null
                && GameState.playing
                && !GameState.inCursorMenu
                && !BoatCamera.on
                && !VrShipyardPatches.IsInShipyard()
                && !VrChartingPatches.IsChartingActive()
                && !rig.HandInteractor.IsAnyHandGrippingAnyRotary()
                && Mathf.Abs(rig.RightHand.Stick.y) > 0.18f)
            {
                __result = rig.RightHand.Stick.y * Time.deltaTime * 4.5f;
                return false;
            }

            __result = Input.GetAxis("Mouse ScrollWheel");
            return false;
        }

        // Prefixes on GameInput.GetKey / GetKeyDown / GetKeyUp completely isolate VR controllers from
        // Sailwind's legacy JoystickButton mappings (which caused Grip/Trigger to fire Jump!),
        // while still allowing physical keyboard keys to work normally.
        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetKey))]
        [HarmonyPrefix]
        private static bool GetKeyPrefix(InputName inputName, ref bool __result)
        {
            if (!IsActiveVr()) return true;
            VrRigController rig = VrRigController.Instance;

            switch (inputName)
            {
                case InputName.CameraMode:
                    __result = IsBoatCameraInput(rig, 0);
                    return false;
                case InputName.Jump:
                    bool rightOnWinch = rig.HandInteractor.IsHandGrippingKind(false, RotaryKind.Winch);
                    __result = (!rightOnWinch && !VrDizzyCompat.IsVrNudgeActive && rig.RightHand.BottomFacePressed) || IsKeyboardKey(inputName, 0);
                    return false;
                case InputName.Crouch:
                    __result = (!VrDizzyCompat.IsVrNudgeActive && rig.RightHand.StickClickPressed) || IsKeyboardKey(inputName, 0);
                    return false;
                case InputName.Run:
                    __result = _sprintToggled || IsKeyboardKey(inputName, 0);
                    return false;
                case InputName.Inventory:
                    bool leftOnWinch = rig.HandInteractor.IsHandGrippingKind(true, RotaryKind.Winch);
                    __result = (!leftOnWinch && rig.LeftHand.BottomFacePressed) || IsKeyboardKey(inputName, 0);
                    return false;
                case InputName.PickUp:
                    // StartMenu uses GameInput.GetKey(PickUp) to dismiss the Early Access disclaimer before GameState.playing.
                    bool disclaimerDismiss = !GameState.playing && (rig.RightHand.TriggerPressed || rig.LeftHand.TriggerPressed || rig.RightHand.BottomFacePressed || rig.LeftHand.BottomFacePressed);
                    __result = disclaimerDismiss || IsKeyboardKey(inputName, 0);
                    return false;
                case InputName.Activate:
                case InputName.Throw:
                    __result = IsKeyboardKey(inputName, 0);
                    return false;
                default:
                    __result = IsKeyboardKey(inputName, 0);
                    return false;
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetKeyDown))]
        [HarmonyPrefix]
        private static bool GetKeyDownPrefix(InputName inputName, ref bool __result)
        {
            if (!IsActiveVr()) return true;
            VrRigController rig = VrRigController.Instance;

            switch (inputName)
            {
                case InputName.CameraMode:
                    __result = IsBoatCameraInput(rig, 1);
                    return false;
                case InputName.Jump:
                    bool rightOnWinchD = rig.HandInteractor.IsHandGrippingKind(false, RotaryKind.Winch);
                    __result = (!rightOnWinchD && !VrDizzyCompat.IsVrNudgeActive && rig.RightHand.BottomFaceDown) || IsKeyboardKey(inputName, 1);
                    return false;
                case InputName.Crouch:
                    // Sailwind's PlayerCrouching toggles crouch on GetKeyDown(InputName.Crouch).
                    __result = (!VrDizzyCompat.IsVrNudgeActive && rig.RightHand.StickClickDown) || IsKeyboardKey(inputName, 1);
                    return false;
                case InputName.Run:
                    __result = rig.LeftHand.StickClickDown || IsKeyboardKey(inputName, 1);
                    return false;
                case InputName.Inventory:
                    bool leftOnWinchD = rig.HandInteractor.IsHandGrippingKind(true, RotaryKind.Winch);
                    __result = (!leftOnWinchD && rig.LeftHand.BottomFaceDown) || IsKeyboardKey(inputName, 1);
                    return false;
                case InputName.PickUp:
                    // Sleep.AnyButtonDown() checks GetKeyDown(PickUp) to leave bed once awake.
                    bool leaveBed = GameState.inBed != null && !GameState.sleeping
                        && (rig.RightHand.TriggerDown || rig.LeftHand.TriggerDown || rig.RightHand.GripDown || rig.LeftHand.GripDown || rig.RightHand.BottomFaceDown || rig.LeftHand.BottomFaceDown);
                    __result = leaveBed || IsKeyboardKey(inputName, 1);
                    return false;
                default:
                    __result = IsKeyboardKey(inputName, 1);
                    return false;
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetKeyUp))]
        [HarmonyPrefix]
        private static bool GetKeyUpPrefix(InputName inputName, ref bool __result)
        {
            if (!IsActiveVr()) return true;
            VrRigController rig = VrRigController.Instance;

            switch (inputName)
            {
                case InputName.CameraMode:
                    __result = IsBoatCameraInput(rig, 2);
                    return false;
                case InputName.Jump:
                    __result = rig.RightHand.BottomFaceUp || IsKeyboardKey(inputName, 2);
                    return false;
                case InputName.Crouch:
                    __result = rig.RightHand.StickClickUp || IsKeyboardKey(inputName, 2);
                    return false;
                case InputName.Run:
                    __result = rig.LeftHand.StickClickUp || IsKeyboardKey(inputName, 2);
                    return false;
                case InputName.Inventory:
                    __result = rig.LeftHand.BottomFaceUp || IsKeyboardKey(inputName, 2);
                    return false;
                case InputName.PickUp:
                    // ShipyardButton.ExtraLateUpdate clears clickHeldButton on MouseButtonUp(0) or GetKeyUp(PickUp).
                    __result = rig.RightHand.TriggerUp || rig.LeftHand.TriggerUp || IsKeyboardKey(inputName, 2);
                    return false;
                default:
                    __result = IsKeyboardKey(inputName, 2);
                    return false;
            }
        }

        // Sailwind's VRSetup asks the Oculus plugin for a headset, finds none under SteamVR, and turns VR look off;
        // SettingsLoader also loads it from the desktop "comfortTurning" setting. With it off, MouseLook turns the
        // camera from mouse input and the start menu is placed along the camera's tilt instead of the player's facing.
        [HarmonyPatch(typeof(VRSetup), "Start")]
        [HarmonyPostfix]
        private static void VrSetupStartPostfix()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) Settings.VRLookEnabled = true;
        }

        [HarmonyPatch(typeof(SettingsLoader), "Awake")]
        [HarmonyPostfix]
        private static void SettingsLoaderAwakePostfix()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) Settings.VRLookEnabled = true;
        }

        [HarmonyPatch(typeof(MouseLook), "Update")]
        [HarmonyPrefix]
        private static bool MouseLookUpdatePrefix()
        {
            // In VR the headset does the looking; never let mouse input turn or tilt the camera.
            return !UnityEngine.XR.XRSettings.isDeviceActive;
        }

        [HarmonyPatch(typeof(Sleep), nameof(Sleep.EnterBed))]
        [HarmonyPostfix]
        private static void SleepEnterBedPostfix(Sleep __instance)
        {
            if (!IsActiveVr() || !Plugin.ComfortSleepHorizonConfig.Value) return;
            KeepTrackingSpaceLevel(__instance);
        }

        [HarmonyPatch(typeof(Sleep), "Update")]
        [HarmonyPostfix]
        private static void SleepUpdatePostfix(Sleep __instance)
        {
            if (!IsActiveVr() || !Plugin.ComfortSleepHorizonConfig.Value || GameState.inBed == null) return;
            KeepTrackingSpaceLevel(__instance);
        }

        private static void KeepTrackingSpaceLevel(Sleep sleep)
        {
            if (sleep == null || sleep.trackingSpace == null) return;
            Vector3 e = sleep.trackingSpace.eulerAngles;
            sleep.trackingSpace.eulerAngles = new Vector3(0f, e.y, 0f);
            MouseLook ml = sleep.trackingSpace.GetComponent<MouseLook>();
            if (ml != null && ml.enabled)
            {
                ml.enabled = false;
            }
        }

        // Sailwind parks its tutorial hint scroll 3 units above the top of a 40-deg desktop monitor
        // instead of deactivating it, which leaves an empty scroll banner visible at the top of VR FOV.
        [HarmonyPatch(typeof(Hints), "Update")]
        [HarmonyPostfix]
        private static void HintsUpdatePostfix(Hints __instance)
        {
            if (!IsActiveVr() || __instance == null) return;
            var textMesh = HintsTextField?.GetValue(__instance) as TextMesh;
            if (textMesh == null) return;

            float timer = HintsTimerField != null ? (float)HintsTimerField.GetValue(__instance) : 0f;
            bool shouldShow = timer > 0f;
            if (textMesh.gameObject.activeSelf != shouldShow)
            {
                textMesh.gameObject.SetActive(shouldShow);
            }
        }
    }
}
