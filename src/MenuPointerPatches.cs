using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    /// <summary>
    /// Routes VR controller laser rays (using 1:1 eye-aligned WorldPosition/WorldRotation)
    /// into Sailwind's 3D menu pointer (MouseButtonPointer) when GameState.inCursorMenu is active.
    /// </summary>
    internal static class MenuPointerPatches
    {
        private const int UiLayerMask = 32;        // Layer 5: UI
        private const int ChartLayerMask = 8388608; // Layer 23: Map/Chart
        private const float MaxMenuRayDistance = 25f;
        private const float HoverDropGraceSeconds = 0.06f;

        private static readonly FieldInfo PointedAtButtonField = AccessTools.Field(typeof(MouseButtonPointer), "pointedAtButton");
        private static readonly FieldInfo LastPointedAtButtonField = AccessTools.Field(typeof(MouseButtonPointer), "lastPointedAtButton");
        private static readonly FieldInfo HitField = AccessTools.Field(typeof(MouseButtonPointer), "hit");

        internal static bool LastRayHitMenu { get; private set; }
        internal static bool LastRayHitLeftHand { get; private set; }
        internal static Vector3 LastRayHitPoint { get; private set; }
        internal static Transform LastRayHitTransform { get; private set; }
        internal static Vector3 LastRayHitLocalPoint { get; private set; }
        internal static bool LastRayHoveredClickable { get; private set; }

        private static bool _useLeftHand;
        private static GoPointerButton _stickyButton;
        private static float _lastStickyHitTime;

        internal static bool IsLeftHandActive => _useLeftHand;

        internal static Vector3 GetResolvedMenuHitPoint()
        {
            return LastRayHitTransform != null
                ? LastRayHitTransform.TransformPoint(LastRayHitLocalPoint)
                : LastRayHitPoint;
        }

        internal static void ResetVisualState()
        {
            LastRayHitMenu = false;
            LastRayHoveredClickable = false;
            LastRayHitTransform = null;
            _stickyButton = null;
        }

        internal static void SetInventoryHoverVisual(bool isLeft, Vector3 hitPoint, bool clickable, Transform hitTransform = null)
        {
            LastRayHitMenu = true;
            LastRayHitLeftHand = isLeft;
            LastRayHitPoint = hitPoint;
            LastRayHitTransform = hitTransform;
            LastRayHitLocalPoint = hitTransform != null ? hitTransform.InverseTransformPoint(hitPoint) : hitPoint;
            LastRayHoveredClickable = clickable;
        }

        internal static void UpdateHandSelection(HandState left, HandState right)
        {
            if (left.TriggerDown && left.HasPose)
            {
                _useLeftHand = true;
            }
            else if (right.TriggerDown && right.HasPose)
            {
                _useLeftHand = false;
            }
            else if (!_useLeftHand && !right.HasPose && left.HasPose)
            {
                _useLeftHand = true;
            }
            else if (_useLeftHand && !left.HasPose && right.HasPose)
            {
                _useLeftHand = false;
            }
        }

        internal static bool TryGetActiveHandRay(out Ray ray, out bool isLeft, out HandState activeHand, out GoPointer activePointer)
        {
            ray = default(Ray);
            isLeft = false;
            activeHand = null;
            activePointer = null;

            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive || !Plugin.EnableVrMenuPointerConfig.Value)
            {
                return false;
            }

            HandState preferred = _useLeftHand ? rig.LeftHand : rig.RightHand;
            HandState fallback = _useLeftHand ? rig.RightHand : rig.LeftHand;

            if (preferred.HasPose)
            {
                activeHand = preferred;
                isLeft = _useLeftHand;
            }
            else if (fallback.HasPose)
            {
                activeHand = fallback;
                isLeft = !_useLeftHand;
            }
            else
            {
                return false;
            }

            activePointer = isLeft ? rig.HandInteractor.Left.Pointer : rig.HandInteractor.Right.Pointer;
            ray = new Ray(activeHand.WorldPosition, activeHand.AimForward);
            return true;
        }

        [HarmonyPatch(typeof(MouseButtonPointer), "DoRaycast")]
        [HarmonyPrefix]
        private static bool DoRaycastPrefix(MouseButtonPointer __instance)
        {
            if (VrChartingPatches.IsChartingActive() && Plugin.EnableVrChartingConfig.Value)
            {
                return false;
            }

            // Sync hand world poses to Camera.main's current-frame boat position right before raycasting
            // so moving-ship Inventory and Logbook menus do not flicker on/off between frames.
            VrRigController.Instance?.RefreshHandPosesNow();

            if (!TryGetActiveHandRay(out Ray ray, out bool isLeft, out _, out _))
            {
                ResetVisualState();
                return true;
            }

            if (__instance.controllerPointer != null && __instance.controllerPointer.gameObject.activeSelf)
            {
                __instance.controllerPointer.gameObject.SetActive(false);
            }

            RaycastHit hitInfo;
            bool didHit = Physics.Raycast(ray, out hitInfo, MaxMenuRayDistance, UiLayerMask)
                || Physics.Raycast(ray, out hitInfo, MaxMenuRayDistance, ChartLayerMask);

            GoPointerButton button = null;
            if (didHit)
            {
                GoPointerButton candidate = hitInfo.collider.GetComponent<GoPointerButton>();
                if (candidate != null && !candidate.unclickable)
                {
                    button = candidate;
                    _stickyButton = candidate;
                    _lastStickyHitTime = Time.unscaledTime;
                    button.Look(null);
                }
                else if (_stickyButton != null && !_stickyButton.unclickable && _stickyButton.isActiveAndEnabled
                    && (Time.unscaledTime - _lastStickyHitTime) <= HoverDropGraceSeconds)
                {
                    button = _stickyButton;
                    button.Look(null);
                }

                LastRayHitMenu = true;
                LastRayHitLeftHand = isLeft;
                LastRayHitPoint = hitInfo.point;
                LastRayHitTransform = hitInfo.transform;
                LastRayHitLocalPoint = hitInfo.transform != null ? hitInfo.transform.InverseTransformPoint(hitInfo.point) : hitInfo.point;
                LastRayHoveredClickable = button != null;
            }
            else if (_stickyButton != null && !_stickyButton.unclickable && _stickyButton.isActiveAndEnabled
                && (Time.unscaledTime - _lastStickyHitTime) <= HoverDropGraceSeconds)
            {
                button = _stickyButton;
                button.Look(null);
            }
            else
            {
                ResetVisualState();
            }

            HitField?.SetValue(__instance, hitInfo);
            PointedAtButtonField?.SetValue(__instance, button);
            return false;
        }

        [HarmonyPatch(typeof(MouseButtonPointer), "LateUpdate")]
        [HarmonyPrefix]
        private static bool LateUpdatePrefix(MouseButtonPointer __instance)
        {
            if (VrChartingPatches.IsChartingActive() && Plugin.EnableVrChartingConfig.Value)
            {
                return false;
            }

            if (!GameState.inCursorMenu)
            {
                ResetVisualState();
                return true;
            }

            if (!TryGetActiveHandRay(out _, out _, out HandState activeHand, out GoPointer activePointer))
            {
                return true;
            }

            // Run raycast in LateUpdate so menus stay responsive even when paused (Time.timeScale == 0).
            DoRaycastPrefix(__instance);

            var pointedAt = PointedAtButtonField?.GetValue(__instance) as GoPointerButton;
            var lastPointed = LastPointedAtButtonField?.GetValue(__instance) as GoPointerButton;

            if (pointedAt != null && pointedAt != lastPointed)
            {
                if (UISoundPlayer.instance != null)
                {
                    UISoundPlayer.instance.PlayUISound(UISounds.buttonHover, 0.6f, 1f);
                }
                activeHand.SendHaptic(0.14f, 0.018f);
            }
            LastPointedAtButtonField?.SetValue(__instance, pointedAt);

            bool clicked = activeHand.TriggerDown || Input.GetMouseButtonDown(0) || GameInput.GetKeyDown(InputName.PickUp);
            if (!clicked || pointedAt == null)
            {
                return false;
            }

            activeHand.SendHaptic(0.32f, 0.03f);
            pointedAt.OnActivate();
            if (UISoundPlayer.instance != null)
            {
                UISoundPlayer.instance.PlayUIClickSound();
            }

            GoPointer targetPointer = activePointer != null ? activePointer : __instance.linkedPointer;
            if (targetPointer != null)
            {
                if (targetPointer.GetHeldItem() != null && !GameState.wasInSettingsMenu)
                {
                    if (pointedAt.OnItemClick(targetPointer.GetHeldItem()))
                    {
                        targetPointer.GetHeldItem().OnDrop();
                        targetPointer.DropItem();
                    }
                }
                else
                {
                    pointedAt.OnActivate(targetPointer);
                    if (HitField != null)
                    {
                        pointedAt.OnActivateHit((RaycastHit)HitField.GetValue(__instance));
                    }
                }
            }

            return false;
        }
    }
}
