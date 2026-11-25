using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    /// <summary>
    /// Adds VR controller camera controls (orbit, pitch, pan, zoom) inside Sailwind's Shipyard
    /// and ensures clean level-horizon restoration when leaving BoatCamera / Shipyard.
    /// </summary>
    internal static class VrShipyardPatches
    {
        private const float StickDeadzone = 0.16f;
        private const float ZoomSpeed = 16f;
        private const float PanSpeed = 10f;
        private const float MinZoom = -40f;
        private const float MaxZoom = -8f;
        private const float MinPitch = -15f;
        private const float MaxPitch = 65f;

        private static readonly FieldInfo BoatCamZoomField = AccessTools.Field(typeof(BoatCamera), "zoomLevel");
        private static readonly FieldInfo BoatCamOffsetField = AccessTools.Field(typeof(BoatCamera), "currentPosOffset");

        internal static bool IsInShipyard()
        {
            return GameState.currentShipyard != null;
        }

        [HarmonyPatch(typeof(ShipyardCameraRotator), "Update")]
        [HarmonyPrefix]
        private static bool ShipyardCameraRotatorUpdatePrefix()
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive || BoatCamera.instance == null)
            {
                return true;
            }

            // Keep desktop MouseLook disabled in VR so mouse movement never fights the headset.
            Transform yawPivot = BoatCamera.instance.transform;
            MouseLook yawMl = yawPivot.GetComponent<MouseLook>();
            if (yawMl != null && yawMl.enabled) yawMl.enabled = false;

            Transform pitchPivot = yawPivot.childCount > 0 ? yawPivot.GetChild(0) : null;
            if (pitchPivot != null)
            {
                MouseLook pitchMl = pitchPivot.GetComponent<MouseLook>();
                if (pitchMl != null && pitchMl.enabled) pitchMl.enabled = false;
            }

            float dt = Time.unscaledDeltaTime;
            float orbitSpeed = Plugin.ShipyardOrbitSpeedConfig.Value;

            // Right thumbstick: orbit yaw (X) and camera pitch (Y) around the boat.
            Vector2 rightStick = rig.RightHand.Stick;
            if (Mathf.Abs(rightStick.x) > StickDeadzone)
            {
                yawPivot.Rotate(Vector3.up, rightStick.x * orbitSpeed * dt, Space.World);
            }

            if (pitchPivot != null && Mathf.Abs(rightStick.y) > StickDeadzone)
            {
                Vector3 e = pitchPivot.localEulerAngles;
                float pitch = e.x > 180f ? e.x - 360f : e.x;
                pitch = Mathf.Clamp(pitch - rightStick.y * (orbitSpeed * 0.7f) * dt, MinPitch, MaxPitch);
                pitchPivot.localEulerAngles = new Vector3(pitch, e.y, 0f);
            }

            // Left thumbstick: zoom in/out (Y) and pan along the boat (X).
            Vector2 leftStick = rig.LeftHand.Stick;
            if (BoatCamZoomField != null && Mathf.Abs(leftStick.y) > StickDeadzone)
            {
                float zoom = (float)BoatCamZoomField.GetValue(BoatCamera.instance);
                zoom = Mathf.Clamp(zoom + leftStick.y * ZoomSpeed * dt, MinZoom, MaxZoom);
                BoatCamZoomField.SetValue(BoatCamera.instance, zoom);
            }

            if (BoatCamOffsetField != null && Mathf.Abs(leftStick.x) > StickDeadzone)
            {
                Vector3 offset = (Vector3)BoatCamOffsetField.GetValue(BoatCamera.instance);
                offset += yawPivot.right * (leftStick.x * PanSpeed * dt);
                offset.x = Mathf.Clamp(offset.x, -25f, 25f);
                offset.z = Mathf.Clamp(offset.z, -25f, 25f);
                BoatCamOffsetField.SetValue(BoatCamera.instance, offset);
            }

            return false;
        }

        [HarmonyPatch(typeof(BoatCamera), nameof(BoatCamera.SwitchOff))]
        [HarmonyPostfix]
        private static void BoatCameraSwitchOffPostfix()
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive) return;

            if (Refs.ovrCameraRig != null)
            {
                Refs.ovrCameraRig.localRotation = Quaternion.identity;
            }
        }
    }
}
