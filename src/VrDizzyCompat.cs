using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    /// <summary>
    /// Optional runtime bridge for Dizzy.FirewoodBundle (Bundle Up) and Dizzy.Clothesline.
    /// Resolves types via reflection only when those mods are loaded; zero hard dependencies.
    /// </summary>
    internal static class VrDizzyCompat
    {
        private static bool _initialized;

        // Dizzy.FirewoodBundle (Bundle Up)
        private static MethodInfo _fwCanTarget;
        private static MethodInfo _fwCountOf;
        private static MethodInfo _fwAddHeldToPile;
        private static MethodInfo _fwCanGatherPiece;
        private static MethodInfo _fwGather;
        private static MethodInfo _fwCanGatherGround;
        private static MethodInfo _fwGatherGround;

        private static MethodInfo _hlCanTarget;
        private static MethodInfo _hlCountOf;
        private static MethodInfo _hlAddHeldToLine;

        // Dizzy.Clothesline
        private static Type _clotheslineLineType;
        private static FieldInfo _clPendingHook;
        private static MethodInfo _clHookTaken;
        private static MethodInfo _clCheckHook;
        private static MethodInfo _clStartLine;
        private static MethodInfo _clFinishLine;
        private static MethodInfo _clCancelPending;
        private static MethodInfo _clCanHang;
        private static MethodInfo _clTryAimHang;
        private static MethodInfo _clHangByPlayer;

        // Dizzy.Nudge
        private static Type _nudgeAxisType;
        private static MethodInfo _nudgeIsTarget;
        private static MethodInfo _nudgeTryDeckAxes;
        private static MethodInfo _nudgeComputeDelta;
        private static MethodInfo _nudgeTryRotate;
        private static MethodInfo _nudgeTryLevel;
        private static MethodInfo _nudgeSyncTwin;
        private static MethodInfo _nudgePlayTap;
        private static MethodInfo _nudgeBroadcastPose;
        private static float _nextNudgeStepTime;

        internal static bool IsVrNudgeActive { get; private set; }

        // Sailwind lamp hook occupancy check
        private static readonly FieldInfo LampHookOccupiedField = AccessTools.Field(typeof(ShipItemLampHook), "occupied");

        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo FindStaticMethod(Type type, string name, params Type[] paramTypes)
        {
            return type != null ? type.GetMethod(name, AnyStatic, null, paramTypes, null) : null;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            Type fwPieces = AccessTools.TypeByName("Dizzy.FirewoodBundle.FirewoodPieces");
            if (fwPieces != null)
            {
                _fwCanTarget = FindStaticMethod(fwPieces, "CanTarget", typeof(ShipItem), typeof(ShipItem));
                _fwCountOf = FindStaticMethod(fwPieces, "CountOf", typeof(ShipItem));
                _fwAddHeldToPile = FindStaticMethod(fwPieces, "AddHeldToPile", typeof(ShipItem), typeof(ShipItem), typeof(GoPointer), typeof(int));
                _fwCanGatherPiece = FindStaticMethod(fwPieces, "CanGatherPiece", typeof(ShipItem));
                _fwGather = FindStaticMethod(fwPieces, "Gather", typeof(ShipItem), typeof(GoPointer));
                _fwCanGatherGround = FindStaticMethod(fwPieces, "CanGatherGround", typeof(ShipItem));
                _fwGatherGround = FindStaticMethod(fwPieces, "GatherGround", typeof(ShipItem), typeof(GoPointer));
                Plugin.Log.LogInfo("[VR] Detected Dizzy.FirewoodBundle (Bundle Up); VR trigger bundling enabled.");
            }

            Type hlPieces = AccessTools.TypeByName("Dizzy.FirewoodBundle.HookLinePieces");
            if (hlPieces != null)
            {
                _hlCanTarget = FindStaticMethod(hlPieces, "CanTarget", typeof(ShipItem), typeof(ShipItem));
                _hlCountOf = FindStaticMethod(hlPieces, "CountOf", typeof(ShipItem));
                _hlAddHeldToLine = FindStaticMethod(hlPieces, "AddHeldToLine", typeof(ShipItem), typeof(ShipItem), typeof(GoPointer), typeof(int));
            }

            Type clLines = AccessTools.TypeByName("Dizzy.Clothesline.Lines");
            _clotheslineLineType = AccessTools.TypeByName("Dizzy.Clothesline.ClotheslineLine");
            if (clLines != null)
            {
                _clPendingHook = clLines.GetField("_pendingHook", AnyStatic);
                _clHookTaken = FindStaticMethod(clLines, "HookTaken", typeof(ShipItem));
                _clCheckHook = FindStaticMethod(clLines, "CheckHook", typeof(ShipItem));
                _clStartLine = FindStaticMethod(clLines, "StartLine", typeof(ShipItem));
                _clFinishLine = FindStaticMethod(clLines, "FinishLine", typeof(ShipItem), typeof(ShipItem));
                _clCancelPending = FindStaticMethod(clLines, "CancelPending", typeof(string));
                _clCanHang = FindStaticMethod(clLines, "CanHang", typeof(PickupableItem));
                if (_clotheslineLineType != null)
                {
                    _clTryAimHang = FindStaticMethod(clLines, "TryAimHang",
                        typeof(Ray), typeof(PickupableItem), typeof(GoPointerButton), typeof(float),
                        _clotheslineLineType.MakeByRefType(), typeof(Vector3).MakeByRefType(), typeof(float).MakeByRefType());
                    _clHangByPlayer = FindStaticMethod(clLines, "HangByPlayer", _clotheslineLineType, typeof(PickupableItem), typeof(Vector3));
                }
                Plugin.Log.LogInfo("[VR] Detected Dizzy.Clothesline; VR hook tie and line hanging enabled.");
            }

            Type nudgeMover = AccessTools.TypeByName("Dizzy.Nudge.NudgeMover");
            _nudgeAxisType = AccessTools.TypeByName("Dizzy.Nudge.NudgeAxis");
            if (nudgeMover != null && _nudgeAxisType != null)
            {
                _nudgeIsTarget = FindStaticMethod(nudgeMover, "IsNudgeTarget", typeof(ShipItem));
                _nudgeTryDeckAxes = FindStaticMethod(nudgeMover, "TryDeckAxes",
                    typeof(ShipItem), typeof(Transform), typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType());
                _nudgeComputeDelta = FindStaticMethod(nudgeMover, "ComputeDelta",
                    _nudgeAxisType, typeof(bool), typeof(Vector3), typeof(Vector3), typeof(Vector3));
                _nudgeTryRotate = FindStaticMethod(nudgeMover, "TryRotate",
                    typeof(ShipItem), _nudgeAxisType, typeof(bool), typeof(Vector3), typeof(Vector3), typeof(Vector3));
                _nudgeTryLevel = FindStaticMethod(nudgeMover, "TryLevel", typeof(ShipItem));
                _nudgeSyncTwin = FindStaticMethod(nudgeMover, "SyncTwin", typeof(ShipItem));
                _nudgePlayTap = FindStaticMethod(nudgeMover, "PlayTap");
                Type coopSync = AccessTools.TypeByName("Dizzy.Nudge.CoopSync");
                _nudgeBroadcastPose = FindStaticMethod(coopSync, "BroadcastPose", typeof(ShipItem));
                Plugin.Log.LogInfo("[VR] Detected Dizzy.Nudge; VR hammer + off-hand Trigger nudge mode enabled.");
            }
        }

        public static bool CanBundleTarget(PickupableItem heldItem, ShipItem targetItem)
        {
            if (!(heldItem is ShipItem heldShip) || targetItem == null || heldShip == targetItem) return false;
            EnsureInitialized();
            try
            {
                if (_fwCanTarget != null && (bool)_fwCanTarget.Invoke(null, new object[] { heldShip, targetItem }))
                {
                    return true;
                }
                if (_hlCanTarget != null && (bool)_hlCanTarget.Invoke(null, new object[] { heldShip, targetItem }))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Bundle target check failed: " + e.GetBaseException().Message);
            }
            return false;
        }

        public static bool TryBundleHeldOnto(PickupableItem heldItem, GoPointerButton targetButton, GoPointer pointer)
        {
            if (!(heldItem is ShipItem heldShip) || targetButton == null || pointer == null) return false;
            ShipItem targetShip = targetButton.GetComponent<ShipItem>();
            if (targetShip == null || targetShip == heldShip) return false;

            EnsureInitialized();
            try
            {
                if (_fwCanTarget != null && _fwAddHeldToPile != null
                    && (bool)_fwCanTarget.Invoke(null, new object[] { heldShip, targetShip }))
                {
                    int count = _fwCountOf != null ? (int)_fwCountOf.Invoke(null, new object[] { heldShip }) : 1;
                    return (bool)_fwAddHeldToPile.Invoke(null, new object[] { heldShip, targetShip, pointer, Math.Max(1, count) });
                }

                if (_hlCanTarget != null && _hlAddHeldToLine != null
                    && (bool)_hlCanTarget.Invoke(null, new object[] { heldShip, targetShip }))
                {
                    int count = _hlCountOf != null ? (int)_hlCountOf.Invoke(null, new object[] { heldShip }) : 1;
                    return (bool)_hlAddHeldToLine.Invoke(null, new object[] { heldShip, targetShip, pointer, Math.Max(1, count) });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Bundle add failed: " + e.GetBaseException().Message);
            }
            return false;
        }

        public static bool TryGatherCrateItem(ShipItem item, GoPointer pointer)
        {
            if (item == null || pointer == null) return false;
            EnsureInitialized();
            if (_fwCanGatherPiece == null || _fwGather == null) return false;

            try
            {
                if ((bool)_fwCanGatherPiece.Invoke(null, new object[] { item }))
                {
                    _fwGather.Invoke(null, new object[] { item, pointer });
                    return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Bundle crate gather failed: " + e.GetBaseException().Message);
            }
            return false;
        }

        public static bool TryGatherLooseItem(ShipItem item, GoPointer pointer)
        {
            if (item == null || pointer == null) return false;
            EnsureInitialized();
            if (_fwCanGatherGround == null || _fwGatherGround == null) return false;

            try
            {
                if ((bool)_fwCanGatherGround.Invoke(null, new object[] { item }))
                {
                    _fwGatherGround.Invoke(null, new object[] { item, pointer });
                    return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Bundle ground gather failed: " + e.GetBaseException().Message);
            }
            return false;
        }

        public static bool TryTieClotheslineOnHook(GoPointerButton targetButton)
        {
            if (!(targetButton is ShipItemLampHook hook)) return false;
            EnsureInitialized();
            if (_clStartLine == null || _clFinishLine == null || _clPendingHook == null) return false;

            try
            {
                // Only create/attach clotheslines on empty, unoccupied hooks.
                bool occupied = LampHookOccupiedField != null && (bool)LampHookOccupiedField.GetValue(hook);
                if (occupied) return false;

                bool taken = _clHookTaken != null && (bool)_clHookTaken.Invoke(null, new object[] { hook });
                if (taken) return false;

                var pending = _clPendingHook.GetValue(null) as ShipItem;
                if (pending == null)
                {
                    if (_clCheckHook != null && !(bool)_clCheckHook.Invoke(null, new object[] { hook }))
                    {
                        return false;
                    }
                    _clStartLine.Invoke(null, new object[] { hook });
                    return true;
                }

                if (pending == hook)
                {
                    _clCancelPending?.Invoke(null, new object[] { "Clothesline cancelled." });
                    return true;
                }

                _clFinishLine.Invoke(null, new object[] { pending, hook });
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clothesline tie failed: " + e.GetBaseException().Message);
            }
            return false;
        }

        // Reusable reflection argument buffers to avoid per-frame array allocations in hot paths.
        private static readonly object[] Args1 = new object[1];
        private static readonly object[] AimHangArgs = new object[7];

        public static bool IsClothesline(GoPointerButton button)
        {
            if (button == null) return false;
            EnsureInitialized();
            return _clotheslineLineType != null && _clotheslineLineType.IsInstanceOfType(button);
        }

        public static bool TryAimClothesline(Ray ray, PickupableItem heldItem, float maxDistance,
            out GoPointerButton clotheslineButton, out Vector3 hitPoint)
        {
            clotheslineButton = null;
            hitPoint = Vector3.zero;
            if (heldItem == null) return false;
            EnsureInitialized();
            if (_clTryAimHang == null || _clCanHang == null) return false;
            // Fast-exit for non-hangable items without reflection calls.
            if (heldItem.GetComponent<HangableItem>() == null) return false;

            try
            {
                Args1[0] = heldItem;
                if (!(bool)_clCanHang.Invoke(null, Args1)) return false;

                // Pass null for currentTarget so ClotheslineLine's own collider never blocks exact rope intersection.
                AimHangArgs[0] = ray;
                AimHangArgs[1] = heldItem;
                AimHangArgs[2] = null;
                AimHangArgs[3] = maxDistance;
                AimHangArgs[4] = null;
                AimHangArgs[5] = Vector3.zero;
                AimHangArgs[6] = 0f;

                if ((bool)_clTryAimHang.Invoke(null, AimHangArgs) && AimHangArgs[4] is GoPointerButton btn)
                {
                    clotheslineButton = btn;
                    hitPoint = (Vector3)AimHangArgs[5];
                    return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clothesline aim failed: " + e.GetBaseException().Message);
            }
            return false;
        }

        public static bool TryHangOnClothesline(GoPointerButton targetButton, PickupableItem heldItem, Ray aimRay, bool hasAimPoint, Vector3 aimPoint)
        {
            if (heldItem == null) return false;
            EnsureInitialized();
            if (_clotheslineLineType == null || _clCanHang == null || _clHangByPlayer == null) return false;
            if (heldItem.GetComponent<HangableItem>() == null) return false;

            try
            {
                Args1[0] = heldItem;
                if (!(bool)_clCanHang.Invoke(null, Args1)) return false;

                // Prefer TryAimHang so the item hangs at the exact spot along the rope where the player is aiming.
                if (_clTryAimHang != null)
                {
                    AimHangArgs[0] = aimRay;
                    AimHangArgs[1] = heldItem;
                    AimHangArgs[2] = null;
                    AimHangArgs[3] = float.MaxValue;
                    AimHangArgs[4] = null;
                    AimHangArgs[5] = Vector3.zero;
                    AimHangArgs[6] = 0f;
                    if ((bool)_clTryAimHang.Invoke(null, AimHangArgs) && AimHangArgs[4] != null)
                    {
                        return (bool)_clHangByPlayer.Invoke(null, new object[] { AimHangArgs[4], heldItem, (Vector3)AimHangArgs[5] });
                    }
                }

                if (targetButton != null && _clotheslineLineType.IsInstanceOfType(targetButton) && hasAimPoint)
                {
                    return (bool)_clHangByPlayer.Invoke(null, new object[] { targetButton, heldItem, aimPoint });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clothesline hang failed: " + e.GetBaseException().Message);
            }
            return false;
        }

        public static bool IsNudgeTarget(ShipItem item)
        {
            if (item == null) return false;
            EnsureInitialized();
            if (_nudgeIsTarget == null) return false;
            try
            {
                return (bool)_nudgeIsTarget.Invoke(null, new object[] { item });
            }
            catch
            {
                return false;
            }
        }

        public static void ClearNudgeState()
        {
            IsVrNudgeActive = false;
        }

        /// <summary>
        /// Drives Dizzy.Nudge when holding a hammer aimed at a nudgeable item while holding the off-hand Trigger:
        /// Left Stick = forward/back + strafe, Right Stick Y = up/down, hold off-hand Grip = rotate,
        /// tap A or Right Stick Click = level.
        /// </summary>
        public static void UpdateVrNudge(VrRigController rig, VrHandSlot leftSlot, VrHandSlot rightSlot)
        {
            IsVrNudgeActive = false;
            if (rig == null || !GameState.playing || GameState.inCursorMenu || GameState.sleeping || BoatCamera.on)
            {
                return;
            }

            EnsureInitialized();
            if (_nudgeIsTarget == null || _nudgeTryDeckAxes == null || _nudgeComputeDelta == null) return;

            VrHandSlot hammerSlot = null;
            VrHandSlot offSlot = null;
            if (rightSlot.HeldItem is ShipItemHammer && leftSlot.Hand.TriggerPressed)
            {
                hammerSlot = rightSlot;
                offSlot = leftSlot;
            }
            else if (leftSlot.HeldItem is ShipItemHammer && rightSlot.Hand.TriggerPressed)
            {
                hammerSlot = leftSlot;
                offSlot = rightSlot;
            }

            if (hammerSlot == null || hammerSlot.HoveredButton == null) return;
            ShipItem target = hammerSlot.HoveredButton.GetComponent<ShipItem>();
            if (target == null || !IsNudgeTarget(target)) return;

            IsVrNudgeActive = true;

            // Tap A or Right Stick Click while holding off-hand Trigger to level the item.
            if (rig.RightHand.BottomFaceDown || rig.RightHand.StickClickDown)
            {
                try
                {
                    if (_nudgeTryLevel != null && (bool)_nudgeTryLevel.Invoke(null, new object[] { target }))
                    {
                        hammerSlot.Hand.SendHaptic(0.28f, 0.03f);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Nudge level failed: " + e.GetBaseException().Message);
                }
                return;
            }

            const float stickThreshold = 0.35f;
            Vector2 leftStick = rig.LeftHand.Stick;
            Vector2 rightStick = rig.RightHand.Stick;

            int axisValue = 0; // 1 = Away, 2 = Vertical, 3 = Strafe
            bool reverse = false;
            float absLY = Mathf.Abs(leftStick.y);
            float absLX = Mathf.Abs(leftStick.x);
            float absRY = Mathf.Abs(rightStick.y);

            if (absRY >= stickThreshold && absRY >= absLY && absRY >= absLX)
            {
                axisValue = 2; // Vertical
                reverse = rightStick.y < 0f;
            }
            else if (absLY >= stickThreshold && absLY >= absLX)
            {
                axisValue = 1; // Away
                reverse = leftStick.y < 0f;
            }
            else if (absLX >= stickThreshold)
            {
                axisValue = 3; // Strafe
                reverse = leftStick.x < 0f;
            }

            if (axisValue == 0)
            {
                _nextNudgeStepTime = 0f;
                return;
            }

            if (Time.unscaledTime < _nextNudgeStepTime) return;
            _nextNudgeStepTime = Time.unscaledTime + 0.09f;

            try
            {
                Transform look = rig.EyeTransform != null ? rig.EyeTransform : (VrRigController.MainCamera != null ? VrRigController.MainCamera.transform : null);
                if (look == null) return;

                object[] axesArgs = { target, look, Vector3.zero, Vector3.zero, Vector3.zero };
                if (!(bool)_nudgeTryDeckAxes.Invoke(null, axesArgs)) return;

                Vector3 up = (Vector3)axesArgs[2];
                Vector3 away = (Vector3)axesArgs[3];
                Vector3 right = (Vector3)axesArgs[4];
                object axisEnum = Enum.ToObject(_nudgeAxisType, axisValue);

                bool rotatedOrMoved = false;
                if (offSlot.Hand.GripPressed && _nudgeTryRotate != null)
                {
                    rotatedOrMoved = (bool)_nudgeTryRotate.Invoke(null, new object[] { target, axisEnum, reverse, up, away, right });
                    if (rotatedOrMoved)
                    {
                        _nudgeBroadcastPose?.Invoke(null, new object[] { target });
                        _nudgePlayTap?.Invoke(null, null);
                    }
                }
                else
                {
                    Vector3 delta = (Vector3)_nudgeComputeDelta.Invoke(null, new object[] { axisEnum, reverse, up, away, right });
                    if (delta.sqrMagnitude > 0f)
                    {
                        target.transform.position += delta;
                        if (_nudgeSyncTwin == null || (bool)_nudgeSyncTwin.Invoke(null, new object[] { target }))
                        {
                            _nudgeBroadcastPose?.Invoke(null, new object[] { target });
                            _nudgePlayTap?.Invoke(null, null);
                            rotatedOrMoved = true;
                        }
                    }
                }

                if (rotatedOrMoved)
                {
                    hammerSlot.Hand.SendHaptic(0.16f, 0.018f);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Nudge step failed: " + e.GetBaseException().Message);
            }
        }
    }
}
