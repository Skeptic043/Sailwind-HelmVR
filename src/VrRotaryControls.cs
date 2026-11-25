using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    internal enum RotaryKind
    {
        None = 0,
        Winch = 1,
        SteeringWheel = 2,
        BilgePump = 3
    }

    internal sealed class RotaryGrabSession
    {
        public GoPointerButton Control;
        public RotaryKind Kind;
        public HandState Hand;

        public Vector3 PrevBoatLocalHandPos;
        public float PendingRateInput;
        public float PendingWheelDeltaDeg;
        public float HapticAccum;
        public float PrevRopeLength;
        public bool HasPrevRopeLength;
    }

    internal static class VrRotaryController
    {
        private const float WinchButtonRate = 34f;
        private const float PumpButtonRate = 32f;
        private const float WheelStickDegPerSec = 110f;
        private const float WheelLateralDegPerMeter = 260f;
        private const float PumpVerticalRatePerMeter = 120f;
        private const float StickDeadzone = 0.18f;

        private static readonly Dictionary<GoPointerButton, RotaryGrabSession> ActiveByControl =
            new Dictionary<GoPointerButton, RotaryGrabSession>();

        private static readonly MethodInfo WheelToggleLock = AccessTools.Method(typeof(GPButtonSteeringWheel), "ToggleLock");
        private static readonly FieldInfo WheelLockedField = AccessTools.Field(typeof(GPButtonSteeringWheel), "locked");

        public static bool IsRotaryControl(GoPointerButton button)
        {
            return button is GPButtonRopeWinch || button is GPButtonSteeringWheel || button is BilgePump;
        }

        public static RotaryGrabSession BeginGrab(GoPointerButton control, HandState hand)
        {
            if (control == null || hand == null) return null;

            RotaryKind kind = RotaryKind.None;
            if (control is GPButtonRopeWinch) kind = RotaryKind.Winch;
            else if (control is GPButtonSteeringWheel) kind = RotaryKind.SteeringWheel;
            else if (control is BilgePump) kind = RotaryKind.BilgePump;
            else return null;

            if (ActiveByControl.ContainsKey(control))
            {
                ActiveByControl.Remove(control);
            }

            Transform refFrame = GetBoatReferenceFrame(control);
            var session = new RotaryGrabSession
            {
                Control = control,
                Kind = kind,
                Hand = hand,
                PrevBoatLocalHandPos = refFrame.InverseTransformPoint(hand.WorldPosition)
            };

            ActiveByControl[control] = session;

            // Unlock steering wheel when freshly grabbed, matching native OnActivate behavior.
            if (kind == RotaryKind.SteeringWheel && WheelLockedField != null && (bool)WheelLockedField.GetValue(control))
            {
                WheelToggleLock?.Invoke(control, null);
            }

            if (UISoundPlayer.instance != null)
            {
                UISoundPlayer.instance.PlayUISound(UISounds.winchClick, 0.33f, 0.85f);
            }
            hand.SendHaptic(0.35f, 0.04f);
            Plugin.Log.LogInfo($"[VR] Grabbed {kind}: '{control.gameObject.name}' with {hand.Label}.");

            return session;
        }

        public static void UpdateSession(RotaryGrabSession session)
        {
            if (session == null || session.Control == null || session.Hand == null || !session.Hand.HasPose)
            {
                return;
            }

            HandState hand = session.Hand;
            Transform refFrame = GetBoatReferenceFrame(session.Control);
            Vector3 curLocalPos = refFrame.InverseTransformPoint(hand.WorldPosition);
            Vector3 localDelta = curLocalPos - session.PrevBoatLocalHandPos;
            session.PrevBoatLocalHandPos = curLocalPos;

            float sens = Plugin.RotarySensitivityConfig.Value;

            if (session.Kind == RotaryKind.Winch)
            {
                // Grip + Trigger = pull in (hoist / tighten), Grip + Primary (A/X) = let out (lower / release),
                // plus gripping hand's thumbstick Y (Down = pull in, Up = release, matching Sailwind's S/W).
                float rate = 0f;
                if (hand.TriggerPressed)
                {
                    rate += WinchButtonRate * sens;
                }
                if (hand.BottomFacePressed)
                {
                    rate -= WinchButtonRate * sens;
                }
                if (Mathf.Abs(hand.Stick.y) > StickDeadzone)
                {
                    rate -= hand.Stick.y * WinchButtonRate * sens;
                }

                session.PendingRateInput = rate;
            }
            else if (session.Kind == RotaryKind.SteeringWheel)
            {
                // Steer smoothly by moving your gripping hand left/right across the helm/tiller
                // OR pushing the gripping hand's thumbstick left/right—no awkward wrist twisting.
                float dirSign = Plugin.InvertSteeringConfig.Value ? 1f : -1f;
                float deltaDeg = dirSign * localDelta.x * WheelLateralDegPerMeter * sens;
                if (Mathf.Abs(hand.Stick.x) > StickDeadzone)
                {
                    deltaDeg += dirSign * hand.Stick.x * WheelStickDegPerSec * sens * Time.deltaTime;
                }

                session.PendingWheelDeltaDeg += deltaDeg;

                session.HapticAccum += Mathf.Abs(deltaDeg);
                if (session.HapticAccum >= 12f)
                {
                    session.HapticAccum = 0f;
                    hand.SendHaptic(0.22f, 0.018f);
                }

                // Trigger while gripping the helm toggles rudder lock.
                if (hand.TriggerDown)
                {
                    WheelToggleLock?.Invoke(session.Control, null);
                    hand.SendHaptic(0.45f, 0.05f);
                }
            }
            else if (session.Kind == RotaryKind.BilgePump)
            {
                float rate = 0f;
                if (hand.TriggerPressed)
                {
                    rate += PumpButtonRate * sens;
                }
                float verticalSpeed = Mathf.Abs(localDelta.y) / Mathf.Max(Time.deltaTime, 0.005f);
                if (verticalSpeed > 0.08f)
                {
                    rate += verticalSpeed * PumpVerticalRatePerMeter * sens * 0.25f;
                }
                session.PendingRateInput = rate;
            }
        }

        public static void EndGrab(RotaryGrabSession session)
        {
            if (session == null) return;
            if (session.Control != null && ActiveByControl.TryGetValue(session.Control, out RotaryGrabSession cur) && cur == session)
            {
                ActiveByControl.Remove(session.Control);
            }
            session.Hand?.SendHaptic(0.18f, 0.025f);
        }

        public static void ClearAll()
        {
            ActiveByControl.Clear();
        }

        public static bool TryGetSession(GoPointerButton control, out RotaryGrabSession session)
        {
            if (control == null)
            {
                session = null;
                return false;
            }
            return ActiveByControl.TryGetValue(control, out session);
        }

        public static float ConsumeWheelDeltaDeg(RotaryGrabSession session)
        {
            float d = session.PendingWheelDeltaDeg;
            session.PendingWheelDeltaDeg = 0f;
            return d;
        }

        private static Transform GetBoatReferenceFrame(GoPointerButton control)
        {
            if (Refs.observerMirror != null)
            {
                return Refs.observerMirror.transform;
            }
            return control.transform.parent != null ? control.transform.parent : control.transform;
        }
    }

    internal static class VrRotaryPatches
    {
        private static readonly FieldInfo WinchCurrentInput = AccessTools.Field(typeof(GPButtonRopeWinch), "currentInput");
        private static readonly FieldInfo WinchRopeEffect = AccessTools.Field(typeof(GPButtonRopeWinch), "ropeEffect");
        private static readonly FieldInfo WinchPullMat = AccessTools.Field(typeof(GPButtonRopeWinch), "pullMaterial");
        private static readonly MethodInfo WinchLimitInput = AccessTools.Method(typeof(GPButtonRopeWinch), "LimitInput");
        private static readonly MethodInfo WinchApplyWind = AccessTools.Method(typeof(GPButtonRopeWinch), "ApplyWindResistance");
        private static readonly MethodInfo WinchLimitTurning = AccessTools.Method(typeof(GPButtonRopeWinch), "LimitTurning");
        private static readonly MethodInfo WinchQuickRelease = AccessTools.Method(typeof(GPButtonRopeWinch), "AddQuickReleaseInput");
        private static readonly MethodInfo WinchGetRotDelta = AccessTools.Method(typeof(GPButtonRopeWinch), "GetRotationDelta");
        private static readonly MethodInfo WinchDeltaToLength = AccessTools.Method(typeof(GPButtonRopeWinch), "DeltaToLength");
        private static readonly MethodInfo WinchSendLength = AccessTools.Method(typeof(GPButtonRopeWinch), "SendLengthToRope");
        private static readonly MethodInfo WinchUpdateAudio = AccessTools.Method(typeof(GPButtonRopeWinch), "UpdateAudio");

        private static readonly FieldInfo WheelLocked = AccessTools.Field(typeof(GPButtonSteeringWheel), "locked");
        private static readonly FieldInfo WheelAudio = AccessTools.Field(typeof(GPButtonSteeringWheel), "audio");
        private static readonly FieldInfo WheelVolumeMult = AccessTools.Field(typeof(GPButtonSteeringWheel), "volumeMult");
        private static readonly FieldInfo WheelLastInput = AccessTools.Field(typeof(GPButtonSteeringWheel), "lastInput");
        private static readonly MethodInfo WheelApplyRotLimit = AccessTools.Method(typeof(GPButtonSteeringWheel), "ApplyRotationLimit");
        private static readonly MethodInfo WheelApplyRudder = AccessTools.Method(typeof(GPButtonSteeringWheel), "ApplyRudderRotation");
        private static readonly MethodInfo WheelApplyVisual = AccessTools.Method(typeof(GPButtonSteeringWheel), "ApplyWheelRotationFromRudder");

        private static readonly FieldInfo PumpCurrentInput = AccessTools.Field(typeof(BilgePump), "currentInput");
        private static readonly FieldInfo PumpAudio = AccessTools.Field(typeof(BilgePump), "audio");
        private static readonly MethodInfo PumpLimitInput = AccessTools.Method(typeof(BilgePump), "LimitInput");

        [HarmonyPatch(typeof(GPButtonRopeWinch), "Update")]
        [HarmonyPrefix]
        private static bool WinchUpdatePrefix(GPButtonRopeWinch __instance)
        {
            if (!VrRotaryController.TryGetSession(__instance, out RotaryGrabSession session))
            {
                return true;
            }

            WinchCurrentInput?.SetValue(__instance, session.PendingRateInput);
            WinchLimitInput?.Invoke(__instance, null);
            WinchApplyWind?.Invoke(__instance, null);
            WinchLimitTurning?.Invoke(__instance, null);
            WinchQuickRelease?.Invoke(__instance, null);
            __instance.ApplyRotation();
            WinchGetRotDelta?.Invoke(__instance, null);
            WinchDeltaToLength?.Invoke(__instance, null);
            WinchSendLength?.Invoke(__instance, null);
            WinchUpdateAudio?.Invoke(__instance, null);

            // Only send winch haptics while the rope is actually moving (stops cleanly at 0% / 100% limits).
            if (__instance.rope != null)
            {
                float curLen = __instance.rope.currentLength;
                if (session.HasPrevRopeLength)
                {
                    float deltaLen = Mathf.Abs(curLen - session.PrevRopeLength);
                    if (deltaLen > 0.0002f)
                    {
                        session.HapticAccum += deltaLen;
                        if (session.HapticAccum >= 0.035f)
                        {
                            session.HapticAccum = 0f;
                            session.Hand?.SendHaptic(0.2f, 0.018f);
                        }
                    }
                }
                session.PrevRopeLength = curLen;
                session.HasPrevRopeLength = true;
            }

            var ropeEffect = WinchRopeEffect?.GetValue(__instance) as LineRenderer;
            var pullMat = WinchPullMat?.GetValue(null) as Material;
            if (ropeEffect != null && pullMat != null && __instance.rope != null)
            {
                ropeEffect.sharedMaterial = pullMat;
                pullMat.mainTextureOffset = new Vector2(__instance.rope.currentLength * 10f, 0f);
            }

            return false;
        }

        [HarmonyPatch(typeof(GPButtonSteeringWheel), nameof(GPButtonSteeringWheel.ExtraLateUpdate))]
        [HarmonyPrefix]
        private static bool WheelExtraLateUpdatePrefix(GPButtonSteeringWheel __instance)
        {
            if (!VrRotaryController.TryGetSession(__instance, out RotaryGrabSession session))
            {
                return true;
            }

            float deltaDeg = VrRotaryController.ConsumeWheelDeltaDeg(session);
            bool locked = WheelLocked != null && (bool)WheelLocked.GetValue(__instance);
            if (!locked)
            {
                __instance.currentInput += deltaDeg;
            }

            WheelApplyRotLimit?.Invoke(__instance, null);
            WheelApplyRudder?.Invoke(__instance, null);
            WheelApplyVisual?.Invoke(__instance, null);

            var audio = WheelAudio?.GetValue(__instance) as AudioSource;
            if (audio != null)
            {
                float lastInput = WheelLastInput != null ? (float)WheelLastInput.GetValue(__instance) : __instance.currentInput;
                float volMult = WheelVolumeMult != null ? (float)WheelVolumeMult.GetValue(__instance) : 1f;
                float speed = Mathf.Abs(__instance.currentInput - lastInput) / Mathf.Max(Time.deltaTime, 0.004f);
                audio.volume = Mathf.Lerp(audio.volume, speed * volMult, Time.deltaTime * 3f);
                if (float.IsNaN(audio.volume)) audio.volume = 0f;
                if (audio.isPlaying && audio.volume <= 0f)
                {
                    audio.Stop();
                    audio.enabled = false;
                }
                else if (audio.volume > 0f && !audio.isPlaying)
                {
                    audio.enabled = true;
                    audio.Play();
                }
            }
            WheelLastInput?.SetValue(__instance, __instance.currentInput);
            return false;
        }

        [HarmonyPatch(typeof(BilgePump), "Update")]
        [HarmonyPrefix]
        private static bool BilgePumpUpdatePrefix(BilgePump __instance)
        {
            if (!VrRotaryController.TryGetSession(__instance, out RotaryGrabSession session))
            {
                return true;
            }

            PumpCurrentInput?.SetValue(__instance, session.PendingRateInput);
            PumpLimitInput?.Invoke(__instance, null);
            __instance.ApplyRotation();

            float clampedInput = PumpCurrentInput != null ? (float)PumpCurrentInput.GetValue(__instance) : 0f;
            var audio = PumpAudio?.GetValue(__instance) as AudioSource;

            if (clampedInput > 0f && __instance.damage != null && !__instance.damage.sunk)
            {
                __instance.damage.waterLevel -= Time.deltaTime * clampedInput * __instance.drainRate;
                if (__instance.damage.waterLevel > 0f)
                {
                    PlayerNeeds.water -= Time.deltaTime * __instance.waterCost * clampedInput;
                    PlayerNeeds.food -= Time.deltaTime * __instance.foodCost * clampedInput;
                    if (PlayerNeedsMinibar.instance != null)
                    {
                        PlayerNeedsMinibar.instance.ShowMinibars(1f, false);
                    }
                    if (audio != null)
                    {
                        audio.volume += Time.deltaTime * 3f;
                    }
                }
            }

            if (audio != null)
            {
                audio.volume -= Time.deltaTime * 2f;
                if (audio.volume <= 0f)
                {
                    audio.Stop();
                    audio.enabled = false;
                }
                else if (!audio.isPlaying)
                {
                    audio.enabled = true;
                    audio.Play();
                }
            }

            return false;
        }
    }
}
