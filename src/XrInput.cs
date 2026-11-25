using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.XR;

namespace HelmVR
{
    internal sealed class HandState
    {
        public readonly XRNode Node;
        public readonly string Label;
        public readonly bool IsLeft;

        public InputDevice Device;
        public bool IsValid;
        public bool HasPose;
        public string PoseSource = "none";

        // Raw XR tracking-space pose & velocity
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;

        // World-space pose & velocity aligned to Sailwind's CenterEyeAnchor tracking space
        public Vector3 WorldPosition;
        public Quaternion WorldRotation = Quaternion.identity;
        public Vector3 WorldVelocity;

        // Physical Quest/Touch A/X (bottom) and B/Y (top); SwapFaceButtons flips them for controllers that report them the other way round.
        public bool BottomFacePressed => Plugin.SwapFaceButtonsConfig.Value ? SecondaryPressed : PrimaryPressed;
        public bool BottomFaceDown => Plugin.SwapFaceButtonsConfig.Value ? SecondaryDown : PrimaryDown;
        public bool BottomFaceUp => Plugin.SwapFaceButtonsConfig.Value ? SecondaryUp : PrimaryUp;

        public bool TopFacePressed => Plugin.SwapFaceButtonsConfig.Value ? PrimaryPressed : SecondaryPressed;
        public bool TopFaceDown => Plugin.SwapFaceButtonsConfig.Value ? PrimaryDown : SecondaryDown;
        public bool TopFaceUp => Plugin.SwapFaceButtonsConfig.Value ? PrimaryUp : SecondaryUp;

        // Comfortably pitched aim orientation (Quest 3 OpenVR forward points ~40 deg too high)
        public Quaternion AimRotation => WorldRotation * Quaternion.Euler(Plugin.AimPitchAngleConfig.Value, 0f, 0f);
        public Vector3 AimForward => AimRotation * Vector3.forward;

        public float Trigger;
        public float Grip;
        public Vector2 Stick;

        public bool TriggerPressed;
        public bool GripPressed;
        public bool PrimaryPressed;
        public bool SecondaryPressed;
        public bool MenuPressed;
        public bool StickClickPressed;

        public bool TriggerDown;
        public bool TriggerUp;
        public bool GripDown;
        public bool GripUp;
        public bool PrimaryDown;
        public bool PrimaryUp;
        public bool SecondaryDown;
        public bool SecondaryUp;
        public bool MenuDown;
        public bool StickClickDown;
        public bool StickClickUp;

        private bool _prevTrigger;
        private bool _prevGrip;
        private bool _prevPrimary;
        private bool _prevSecondary;
        private bool _prevMenu;
        private bool _prevStickClick;
        private bool _wasValid;
        private string _lastDeviceName = string.Empty;

        public HandState(XRNode node, string label, bool isLeft)
        {
            Node = node;
            Label = label;
            IsLeft = isLeft;
        }

        public void UpdateInput(bool logEvents, ManualLogSource log)
        {
            Device = InputDevices.GetDeviceAtXRNode(Node);
            IsValid = Device.isValid;

            string currentName = IsValid ? (Device.name ?? "<unnamed>") : string.Empty;
            if (IsValid != _wasValid || currentName != _lastDeviceName)
            {
                _wasValid = IsValid;
                _lastDeviceName = currentName;
                if (IsValid)
                {
                    log.LogInfo($"[XR] {Label} connected: '{currentName}' role={Device.role}");
                }
                else
                {
                    log.LogInfo($"[XR] {Label} disconnected.");
                }
            }

            HasPose = false;
            PoseSource = "none";
            if (IsValid
                && Device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 devPos)
                && Device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion devRot))
            {
                LocalPosition = devPos;
                LocalRotation = devRot;
                HasPose = true;
                PoseSource = "InputDevices";
            }
            else
            {
                Vector3 legacyPos = InputTracking.GetLocalPosition(Node);
                Quaternion legacyRot = InputTracking.GetLocalRotation(Node);
                if (legacyPos.sqrMagnitude > 0.000001f || Quaternion.Angle(legacyRot, Quaternion.identity) > 0.01f)
                {
                    LocalPosition = legacyPos;
                    LocalRotation = legacyRot;
                    HasPose = true;
                    PoseSource = "InputTracking";
                }
            }

            if (IsValid)
            {
                Device.TryGetFeatureValue(CommonUsages.deviceVelocity, out Velocity);
                Device.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out AngularVelocity);
                Device.TryGetFeatureValue(CommonUsages.trigger, out Trigger);
                Device.TryGetFeatureValue(CommonUsages.grip, out Grip);
                Device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Stick);

                bool trigBtn;
                if (!Device.TryGetFeatureValue(CommonUsages.triggerButton, out trigBtn))
                {
                    trigBtn = Trigger > 0.7f;
                }

                bool gripBtn;
                if (!Device.TryGetFeatureValue(CommonUsages.gripButton, out gripBtn))
                {
                    gripBtn = Grip > 0.7f;
                }

                Device.TryGetFeatureValue(CommonUsages.primaryButton, out PrimaryPressed);
                Device.TryGetFeatureValue(CommonUsages.secondaryButton, out SecondaryPressed);
                Device.TryGetFeatureValue(CommonUsages.menuButton, out MenuPressed);
                Device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out StickClickPressed);
                TriggerPressed = trigBtn;
                GripPressed = gripBtn;
            }
            else
            {
                Velocity = Vector3.zero;
                AngularVelocity = Vector3.zero;
                Trigger = 0f;
                Grip = 0f;
                Stick = Vector2.zero;
                TriggerPressed = false;
                GripPressed = false;
                PrimaryPressed = false;
                SecondaryPressed = false;
                MenuPressed = false;
                StickClickPressed = false;
            }

            TriggerDown = TriggerPressed && !_prevTrigger;
            TriggerUp = !TriggerPressed && _prevTrigger;
            GripDown = GripPressed && !_prevGrip;
            GripUp = !GripPressed && _prevGrip;
            PrimaryDown = PrimaryPressed && !_prevPrimary;
            PrimaryUp = !PrimaryPressed && _prevPrimary;
            SecondaryDown = SecondaryPressed && !_prevSecondary;
            SecondaryUp = !SecondaryPressed && _prevSecondary;
            MenuDown = MenuPressed && !_prevMenu;
            StickClickDown = StickClickPressed && !_prevStickClick;
            StickClickUp = !StickClickPressed && _prevStickClick;

            if (logEvents)
            {
                LogEdge(log, "Trigger", TriggerPressed, _prevTrigger);
                LogEdge(log, "Grip", GripPressed, _prevGrip);
                LogEdge(log, "Primary(A/X)", PrimaryPressed, _prevPrimary);
                LogEdge(log, "Secondary(B/Y)", SecondaryPressed, _prevSecondary);
                LogEdge(log, "Menu", MenuPressed, _prevMenu);
                LogEdge(log, "StickClick", StickClickPressed, _prevStickClick);
            }

            _prevTrigger = TriggerPressed;
            _prevGrip = GripPressed;
            _prevPrimary = PrimaryPressed;
            _prevSecondary = SecondaryPressed;
            _prevMenu = MenuPressed;
            _prevStickClick = StickClickPressed;
        }

        public void UpdateWorldPose(Transform trackingRoot, Vector3 headOriginOffset)
        {
            if (!HasPose || trackingRoot == null) return;

            // trackingRoot is CenterEyeAnchor (non-head-rotating rig space under UUVR RelativeMatrix).
            // Subtracting the calibrated head origin aligns both eyes and hands 1:1 in world space
            // without ever inverting hand rotation when you turn your head.
            WorldPosition = trackingRoot.TransformPoint(LocalPosition - headOriginOffset);
            WorldRotation = trackingRoot.rotation * LocalRotation;
            WorldVelocity = trackingRoot.rotation * Velocity;
        }

        public void SendHaptic(float normalizedAmplitude, float durationSeconds)
        {
            if (!IsValid || !Plugin.EnableHapticsConfig.Value) return;
            float amp = Mathf.Clamp01(normalizedAmplitude * Plugin.HapticStrengthConfig.Value);
            if (amp <= 0.001f) return;
            Device.SendHapticImpulse(0u, amp, durationSeconds);
        }

        private void LogEdge(ManualLogSource log, string button, bool current, bool previous)
        {
            if (current != previous)
            {
                log.LogInfo($"[XR] {Label} {button}: {(current ? "DOWN" : "UP")} (world={WorldPosition.ToString("F2")})");
            }
        }

        public void LogCapabilities(ManualLogSource log)
        {
            if (!IsValid)
            {
                log.LogInfo($"[XR] {Label}: device not valid.");
                return;
            }

            var usages = new List<InputFeatureUsage>();
            if (Device.TryGetFeatureUsages(usages))
            {
                var sb = new StringBuilder();
                for (int i = 0; i < usages.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(usages[i].name);
                }
                log.LogInfo($"[XR] {Label} usages ({usages.Count}): {sb}");
            }

            if (Device.TryGetHapticCapabilities(out HapticCapabilities haptics))
            {
                log.LogInfo($"[XR] {Label} haptics: impulse={haptics.supportsImpulse}, buffer={haptics.supportsBuffer}, channels={haptics.numChannels}");
            }
        }
    }

    internal sealed class XrInputReader
    {
        public readonly HandState Left = new HandState(XRNode.LeftHand, "LeftHand", true);
        public readonly HandState Right = new HandState(XRNode.RightHand, "RightHand", false);

        public Vector3 HeadLocalPosition { get; private set; }
        public Quaternion HeadLocalRotation { get; private set; } = Quaternion.identity;
        public bool HasHeadPose { get; private set; }

        public Vector3 EyeWorldPosition { get; private set; }
        public Quaternion EyeWorldRotation { get; private set; } = Quaternion.identity;
        public Transform EyeTransform { get; private set; }

        private bool _hasCalibratedHead;
        private bool _configuredTrackingSpace;
        private Vector3 _calibratedHeadOrigin;
        public float CalibratedHeadYaw { get; private set; }

        public void Update(bool logEvents, ManualLogSource log)
        {
            if (!_configuredTrackingSpace && XRSettings.isDeviceActive)
            {
                _configuredTrackingSpace = true;
                // Stationary tracking space allows OpenVR InputTracking.Recenter() to zero seated yaw & position,
                // keeping Main Menu, Pause, Inventory, and Logbook centered in front of you.
                if (XRDevice.GetTrackingSpaceType() != TrackingSpaceType.Stationary)
                {
                    XRDevice.SetTrackingSpaceType(TrackingSpaceType.Stationary);
                }
                InputTracking.Recenter();
            }

            UpdateHeadPose();
            Left.UpdateInput(logEvents, log);
            Right.UpdateInput(logEvents, log);
            RefreshWorldPoses();
        }

        // Recentering is two steps: the tracking reset takes effect on a later frame, so the head pose must be
        // captured afterwards (CaptureHeadOrigin), or the old offset is read back and kept.
        public void ResetTracking()
        {
            if (XRDevice.GetTrackingSpaceType() != TrackingSpaceType.Stationary)
            {
                XRDevice.SetTrackingSpaceType(TrackingSpaceType.Stationary);
            }
            InputTracking.Recenter();
        }

        public void CaptureHeadOrigin()
        {
            UpdateHeadPose();
            if (HasHeadPose)
            {
                _calibratedHeadOrigin = HeadLocalPosition;
                CalibratedHeadYaw = HeadLocalRotation.eulerAngles.y;
                _hasCalibratedHead = true;
            }
        }

        public void RefreshWorldPoses()
        {
            UpdateHeadPose();

            Camera mainCam = VrRigController.MainCamera;
            if (mainCam == null)
            {
                EyeTransform = null;
                return;
            }

            if (HasHeadPose && !_hasCalibratedHead)
            {
                _calibratedHeadOrigin = HeadLocalPosition;
                CalibratedHeadYaw = HeadLocalRotation.eulerAngles.y;
                _hasCalibratedHead = true;
            }

            Transform root = mainCam.transform;
            EyeTransform = root;
            Vector3 headOffset = _hasCalibratedHead ? _calibratedHeadOrigin : Vector3.zero;

            EyeWorldPosition = root.TransformPoint(HeadLocalPosition - headOffset);
            EyeWorldRotation = root.rotation * HeadLocalRotation;

            Left.UpdateWorldPose(root, headOffset);
            Right.UpdateWorldPose(root, headOffset);
        }

        private void UpdateHeadPose()
        {
            InputDevice headDev = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!headDev.isValid)
            {
                headDev = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            }

            if (headDev.isValid
                && headDev.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 cPos)
                && headDev.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion cRot))
            {
                HeadLocalPosition = cPos;
                HeadLocalRotation = cRot;
                HasHeadPose = true;
                return;
            }

            if (headDev.isValid
                && headDev.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 dPos)
                && headDev.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion dRot))
            {
                HeadLocalPosition = dPos;
                HeadLocalRotation = dRot;
                HasHeadPose = true;
                return;
            }

            Vector3 legacyPos = InputTracking.GetLocalPosition(XRNode.CenterEye);
            Quaternion legacyRot = InputTracking.GetLocalRotation(XRNode.CenterEye);
            HeadLocalPosition = legacyPos;
            HeadLocalRotation = legacyRot;
            HasHeadPose = legacyPos.sqrMagnitude > 0.000001f || Quaternion.Angle(legacyRot, Quaternion.identity) > 0.01f;
        }

        public void DumpAll(ManualLogSource log)
        {
            log.LogInfo("=== HelmVR XR Diagnostics ===");
            log.LogInfo($"XRSettings: enabled={XRSettings.enabled}, isDeviceActive={XRSettings.isDeviceActive}, loaded='{XRSettings.loadedDeviceName}', stereoMode={XRSettings.stereoRenderingMode}, eye={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, resScale={XRSettings.eyeTextureResolutionScale:F2}");
            log.LogInfo($"XRDevice: isPresent={XRDevice.isPresent}, model='{XRDevice.model}', refresh={XRDevice.refreshRate:F1}Hz, trackingSpace={XRDevice.GetTrackingSpaceType()}");
            log.LogInfo($"Head: local={HeadLocalPosition.ToString("F2")}, calibratedOrigin={_calibratedHeadOrigin.ToString("F2")}, eyeWorld={EyeWorldPosition.ToString("F2")}");
            log.LogInfo($"LeftHand: pose={Left.PoseSource}, local={Left.LocalPosition.ToString("F2")}, world={Left.WorldPosition.ToString("F2")}");
            log.LogInfo($"RightHand: pose={Right.PoseSource}, local={Right.LocalPosition.ToString("F2")}, world={Right.WorldPosition.ToString("F2")}");
        }
    }
}
