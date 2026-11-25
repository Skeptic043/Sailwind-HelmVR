using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    internal sealed class VrHandSlot
    {
        public readonly bool IsLeft;
        public readonly HandState Hand;

        public GoPointer Pointer;
        public GoPointerButton HoveredButton;
        public GoPointerButton ActiveClickedButton;
        public RotaryGrabSession ActiveRotary;
        public GPButtonInventorySlot LastHoveredInvSlot;

        public bool IsNearTarget;
        public bool HasRayHit;
        // True while the held item is previewed on a surface that allows placing; releasing it then sets it down there.
        public bool IsPlacing;
        public Vector3 RayHitPoint;
        public Transform RayHitTransform;
        public Vector3 RayHitLocalPoint;
        public RaycastHit LastRaycastHit;

        public Vector3 GetResolvedRayHitPoint()
        {
            return RayHitTransform != null
                ? RayHitTransform.TransformPoint(RayHitLocalPoint)
                : RayHitPoint;
        }

        // Held item grip offsets
        private static readonly FieldInfo FishingRodSpinnerField = AccessTools.Field(typeof(ShipItemFishingRod), "spinner");
        private PickupableItem _trackedHeld;
        private Quaternion _heldRotOffset = Quaternion.identity;
        private Vector3 _heldPosOffset = new Vector3(0f, 0f, 0.04f);

        public VrHandSlot(bool isLeft, HandState hand)
        {
            IsLeft = isLeft;
            Hand = hand;
        }

        public PickupableItem HeldItem => Pointer != null ? Pointer.GetHeldItem() : null;

        public void SyncHeldItemTransform()
        {
            PickupableItem held = HeldItem;
            IsPlacing = false;
            if (held == null)
            {
                _trackedHeld = null;
                return;
            }

            if (held != _trackedHeld)
            {
                _trackedHeld = held;
                if (held.big)
                {
                    _heldPosOffset = new Vector3(0f, -0.05f, 0.45f);
                    _heldRotOffset = Quaternion.Inverse(Hand.WorldRotation) * held.transform.rotation;
                }
                else if (held is ShipItemFishingRod)
                {
                    // Place fishing rod handle directly in the palm pointing forward.
                    _heldPosOffset = new Vector3(0f, -0.03f, 0.06f);
                    _heldRotOffset = Quaternion.identity;
                }
                else if (held is ShipItemHammer)
                {
                    // Hold hammer upright and forward, ready to swing.
                    _heldPosOffset = new Vector3(0f, 0.02f, 0.06f);
                    _heldRotOffset = Quaternion.identity;
                }
                else
                {
                    // Canonical upright/forward in-hand pose for usable items (spyglass, compass, lantern, broom, bottles, food, charting kit).
                    _heldPosOffset = new Vector3(0f, 0f, 0.05f);
                    _heldRotOffset = Quaternion.identity;
                }
            }

            if (!Hand.HasPose) return;

            bool isTool = held is ShipItemHammer || held is ShipItemInkSet || held is ShipItemFishingRod;
            // Like native GoPointer, a small item over any surface that allows placing (tables, shelves, crate lids)
            // sits on that surface until released. Only the hand's heading is kept so the item stands upright.
            if (!held.big && !isTool && HoveredButton != null && HoveredButton.allowPlacingItems && HasRayHit)
            {
                Vector3 flatForward = Vector3.ProjectOnPlane(Hand.AimForward, Vector3.up);
                Quaternion heading = flatForward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flatForward, Vector3.up) : Quaternion.identity;
                held.transform.position = GetResolvedRayHitPoint() + Vector3.up * held.furniturePlaceHeight;
                held.transform.rotation = heading * _heldRotOffset;
                held.forceDisableRedOutline = true;
                IsPlacing = true;
                return;
            }

            held.forceDisableRedOutline = false;
            Quaternion baseRot = held.big ? Hand.WorldRotation : Hand.AimRotation;
            Quaternion swingRot = Mathf.Abs(held.heldRotationOffset) > 0.01f
                ? Quaternion.Euler(held.heldRotationOffset, 0f, 0f)
                : Quaternion.identity;
            held.transform.rotation = baseRot * _heldRotOffset * swingRot;

            if (held is ShipItemFishingRod rod && FishingRodSpinnerField?.GetValue(rod) is Transform spinner)
            {
                // Anchor the fishing rod so its reel/handle sits in your palm, slightly inward and forward.
                Vector3 desiredSpinnerPos = Hand.WorldPosition + baseRot * new Vector3(IsLeft ? 0.04f : -0.04f, 0.01f, 0.09f);
                Vector3 rodToSpinner = spinner.position - held.transform.position;
                held.transform.position = desiredSpinnerPos - rodToSpinner;
            }
            else
            {
                held.transform.position = Hand.WorldPosition + baseRot * _heldPosOffset;
            }
        }

        public void ResetFrameTarget()
        {
            HoveredButton = null;
            IsNearTarget = false;
            HasRayHit = false;
            RayHitTransform = null;
        }
    }

    internal sealed class VrHandInteractor
    {
        private const int InteractionLayerMask = -67713029; // Matches GoPointer.DoRaycast() (excludes Layer 26 crate contents)
        private const int UiLayerMask = 32;               // Layer 5: UI (inventory slots)
        private const float ThrowSpeedThreshold = 0.75f;
        private const float MaxRotaryGrabDistance = 2.5f;

        private const float PointerSearchRetrySeconds = 0.25f;

        private static readonly Collider[] OverlapBuffer = new Collider[48];
        private static readonly List<GoPointerButton> ChildButtonBuffer = new List<GoPointerButton>();
        private static readonly FieldInfo GoPointerPointedAtField = AccessTools.Field(typeof(GoPointer), "pointedAtButton");
        private static readonly FieldInfo GoPointerLookUiField = AccessTools.Field(typeof(GoPointer), "lookUI");
        private static readonly FieldInfo CrateButtonItemField = AccessTools.Field(typeof(CrateInventoryButton), "currentItem");

        private static ShipItem GetCrateButtonItem(CrateInventoryButton btn)
        {
            return btn != null ? CrateButtonItemField?.GetValue(btn) as ShipItem : null;
        }

        public readonly VrHandSlot Left;
        public readonly VrHandSlot Right;

        private readonly MonoBehaviour _coroutineHost;
        private readonly HashSet<GoPointer> _ownedPointers = new HashSet<GoPointer>();

        private GoPointer _desktopPointer;
        private bool _desktopPointerDisabled;
        private LookUI _lookUi;
        private float _nextPointerSearchTime;

        public VrHandInteractor(MonoBehaviour coroutineHost, HandState leftHand, HandState rightHand)
        {
            _coroutineHost = coroutineHost;
            Left = new VrHandSlot(true, leftHand);
            Right = new VrHandSlot(false, rightHand);
        }

        public bool IsOwnedPointer(GoPointer pointer)
        {
            return pointer != null && _ownedPointers.Contains(pointer);
        }

        public bool IsVrHeldItem(PickupableItem item)
        {
            return item != null && item.held != null && _ownedPointers.Contains(item.held);
        }

        public bool IsAnyHandGrippingWinch()
        {
            return IsHandGrippingKind(true, RotaryKind.Winch) || IsHandGrippingKind(false, RotaryKind.Winch);
        }

        public bool IsHandGrippingKind(bool isLeft, RotaryKind kind)
        {
            VrHandSlot slot = isLeft ? Left : Right;
            return slot != null && slot.ActiveRotary != null && slot.ActiveRotary.Kind == kind;
        }

        public bool IsHandGrippingAnyRotary(bool isLeft)
        {
            VrHandSlot slot = isLeft ? Left : Right;
            return slot != null && slot.ActiveRotary != null;
        }

        public bool IsAnyHandGrippingAnyRotary()
        {
            return (Left != null && Left.ActiveRotary != null) || (Right != null && Right.ActiveRotary != null);
        }

        public void ResetSceneReferences()
        {
            VrRotaryController.ClearAll();
            Left.Pointer = null;
            Right.Pointer = null;
            Left.ActiveRotary = null;
            Right.ActiveRotary = null;
            Left.ActiveClickedButton = null;
            Right.ActiveClickedButton = null;
            Left.ResetFrameTarget();
            Right.ResetFrameTarget();
            _ownedPointers.Clear();
            _desktopPointer = null;
            _desktopPointerDisabled = false;
            _lookUi = null;
            _nextPointerSearchTime = 0f;
        }

        public void RestoreDesktopPointer()
        {
            VrRotaryController.ClearAll();
            if (_desktopPointer != null && _desktopPointerDisabled)
            {
                _desktopPointer.gameObject.SetActive(true);
                _desktopPointerDisabled = false;
            }
        }

        public void Update()
        {
            if (!Plugin.EnableMotionControlsConfig.Value)
            {
                RestoreDesktopPointer();
                return;
            }

            EnsurePointersResolved();
            DisableDesktopPointerIfNeeded();

            UpdateHand(Left, Right);
            UpdateHand(Right, Left);
        }

        public void LateUpdate()
        {
            if (!Plugin.EnableMotionControlsConfig.Value) return;

            SyncPointerPose(Left);
            SyncPointerPose(Right);
            Left.SyncHeldItemTransform();
            Right.SyncHeldItemTransform();

            UpdateLookUi();
        }

        private void EnsurePointersResolved()
        {
            if (Left.Pointer != null && Right.Pointer != null && _desktopPointer != null)
            {
                return;
            }

            // FindObjectsOfTypeAll walks every loaded object, so retry a few times a second rather than every frame.
            if (Time.unscaledTime < _nextPointerSearchTime) return;
            _nextPointerSearchTime = Time.unscaledTime + PointerSearchRetrySeconds;

            GoPointer[] allPointers = Resources.FindObjectsOfTypeAll<GoPointer>();
            foreach (GoPointer gp in allPointers)
            {
                if (gp == null || !gp.gameObject.scene.IsValid()) continue;

                if (gp.type == GoPointer.PointerType.crosshairMouse && _desktopPointer == null)
                {
                    _desktopPointer = gp;
                    if (_lookUi == null)
                    {
                        _lookUi = GoPointerLookUiField?.GetValue(gp) as LookUI;
                    }
                }
                else if (gp.type == GoPointer.PointerType.leftTouch && Left.Pointer == null)
                {
                    Left.Pointer = PrepareTouchPointer(gp);
                    _ownedPointers.Add(Left.Pointer);
                }
                else if (gp.type == GoPointer.PointerType.rightTouch && Right.Pointer == null)
                {
                    Right.Pointer = PrepareTouchPointer(gp);
                    _ownedPointers.Add(Right.Pointer);
                }
            }
        }

        private static GoPointer PrepareTouchPointer(GoPointer gp)
        {
            if (!gp.gameObject.activeSelf)
            {
                gp.gameObject.SetActive(true);
            }

            // Detach from Oculus hand anchor so our eye-aligned WorldPosition/Rotation drives it cleanly.
            Transform observer = Refs.observerMirror != null ? Refs.observerMirror.transform : null;
            if (observer != null && gp.transform.parent != observer)
            {
                gp.transform.SetParent(observer, true);
            }

            // Hide Sailwind's legacy Oculus Touch wood/rope pointer beam ("trail") and crosshair
            // without touching unrelated scene children like "cargo carriers".
            if (gp.pointer != null && gp.pointer.gameObject.activeSelf)
            {
                gp.pointer.gameObject.SetActive(false);
            }
            for (int i = 0; i < gp.transform.childCount; i++)
            {
                Transform child = gp.transform.GetChild(i);
                string n = child.name.ToLowerInvariant();
                if (n.Contains("trail") || n.Contains("crosshair"))
                {
                    child.gameObject.SetActive(false);
                }
            }

            return gp;
        }

        private void DisableDesktopPointerIfNeeded()
        {
            if (_desktopPointer != null && GameState.playing && _desktopPointer.gameObject.activeSelf)
            {
                _desktopPointer.gameObject.SetActive(false);
                _desktopPointerDisabled = true;
            }
        }

        private static void SyncPointerPose(VrHandSlot slot)
        {
            if (slot.Pointer == null || !slot.Hand.HasPose) return;
            slot.Pointer.transform.position = slot.Hand.WorldPosition;
            slot.Pointer.transform.rotation = slot.Hand.WorldRotation;
        }

        private void UpdateHand(VrHandSlot slot, VrHandSlot otherSlot)
        {
            SyncPointerPose(slot);

            if (!slot.Hand.HasPose || !GameState.playing || GameState.sleeping || GameState.inBed != null || BoatCamera.on)
            {
                ReleaseRotaryIfActive(slot);
                ReleaseClickedButtonIfActive(slot);
                slot.ResetFrameTarget();
                return;
            }

            // When Inventory is open, support natural Grip drop-to-deposit and Grip grab-to-withdraw!
            if (GameState.inCursorMenu)
            {
                ReleaseRotaryIfActive(slot);
                ReleaseClickedButtonIfActive(slot);
                slot.ResetFrameTarget();
                HandleInventoryGrip(slot);
                return;
            }

            // 1. If currently gripping a rotary control (winch/wheel/pump), update it until released or out of reach.
            if (slot.ActiveRotary != null)
            {
                bool shouldRelease = ShouldReleaseRotary(slot);
                bool outOfReach = slot.ActiveRotary.Control == null
                    || Vector3.Distance(slot.Hand.WorldPosition, slot.ActiveRotary.Control.transform.position) > MaxRotaryGrabDistance;
                if (shouldRelease || outOfReach)
                {
                    ReleaseRotaryIfActive(slot);
                    // Return immediately so ToggleGrip release does not fall through and re-grab on the same frame.
                    return;
                }
                else
                {
                    VrRotaryController.UpdateSession(slot.ActiveRotary);
                    slot.HoveredButton = slot.ActiveRotary.Control;
                    slot.IsNearTarget = true;
                    slot.HasRayHit = false;
                    return;
                }
            }

            // 2. Find hovered GoPointerButton (child buttons on other hand's held item, near-hand proximity sphere, short ray fallback).
            GoPointerButton prevHovered = slot.HoveredButton;
            FindTarget(slot, otherSlot);

            if (slot.HoveredButton != null)
            {
                slot.HoveredButton.Look(slot.Pointer);
                if (slot.HoveredButton != prevHovered)
                {
                    slot.Hand.SendHaptic(0.12f, 0.018f);
                }
            }
            if (slot.Pointer != null)
            {
                GoPointerPointedAtField?.SetValue(slot.Pointer, slot.HoveredButton);
            }

            // 3. Handle Grip (pick up / drop / throw items, hand-to-hand swap, or grab rotary controls).
            HandleGrip(slot, otherSlot);

            // 4. Handle Trigger (click buttons, buy/open/use, item-on-item, alt-activate held item).
            HandleTrigger(slot);

            // 5. Handle Right Thumbstick Y scroll on held items (fishing rod reel, spyglass zoom, compass bezel, scrolls)
            //    or hovered scrollable controls (mooring rope length adjuster).
            HandleScrollInput(slot);
        }

        private void HandleScrollInput(VrHandSlot slot)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null) return;

            // Only drive scroll once per frame from the hand that holds a scrollable item (or right hand fallback).
            PickupableItem held = slot.HeldItem;
            float stickY = rig.RightHand.Stick.y;
            if (Mathf.Abs(stickY) <= 0.18f) return;

            if (held != null)
            {
                // Avoid double-scrolling if both hands hold items: right hand takes priority.
                if (slot.IsLeft && Right.HeldItem != null) return;
                float scrollSpeed = held is ShipItemFishingRod ? Plugin.FishingReelSpeedConfig.Value : 18f;
                held.OnScroll(stickY * Time.deltaTime * scrollSpeed);
            }
            else if (slot.HoveredButton is MooringRopeLengthAdjuster ropeAdj)
            {
                ropeAdj.OnScroll(stickY * Time.deltaTime * 18f);
            }
        }

        private void HandleInventoryGrip(VrHandSlot slot)
        {
            if (slot.Pointer == null || PlayerNeedsUI.instance == null || !PlayerNeedsUI.instance.IsActive())
            {
                slot.LastHoveredInvSlot = null;
                return;
            }

            PickupableItem held = slot.HeldItem;
            GPButtonInventorySlot invSlot = FindInventorySlotForHand(slot, out Vector3 slotHitPoint);

            bool validDepositTarget = held != null && invSlot != null && invSlot.currentItem == null && !held.big;
            bool validWithdrawTarget = held == null && invSlot != null && invSlot.currentItem != null;

            if (validDepositTarget || validWithdrawTarget)
            {
                invSlot.Look(slot.Pointer);
                MenuPointerPatches.SetInventoryHoverVisual(slot.IsLeft, slotHitPoint, true, invSlot.transform);
                if (invSlot != slot.LastHoveredInvSlot)
                {
                    slot.Hand.SendHaptic(0.24f, 0.025f);
                }
                slot.LastHoveredInvSlot = invSlot;
            }
            else
            {
                slot.LastHoveredInvSlot = null;
            }

            // Drop-to-deposit: releasing Grip over an empty inventory slot deposits the held item.
            if (held != null)
            {
                if (!ShouldReleaseHeldItem(slot, held)) return;

                if (invSlot != null && invSlot.OnItemClick(held))
                {
                    held.OnDrop();
                    slot.Pointer.DropItem();
                    slot.Hand.SendHaptic(0.35f, 0.04f);
                }
                else
                {
                    DropAndMaybeThrow(slot, held);
                }
                return;
            }

            // Grip-to-withdraw: pressing Grip over an occupied inventory slot grabs it straight into your hand.
            if (slot.Hand.GripDown && invSlot != null && invSlot.currentItem != null)
            {
                invSlot.OnActivate(slot.Pointer);
                slot.Hand.SendHaptic(0.35f, 0.04f);
            }
        }

        private static GPButtonInventorySlot FindInventorySlotForHand(VrHandSlot slot, out Vector3 hitPoint)
        {
            Vector3 origin = slot.Hand.WorldPosition;
            Vector3 forward = slot.Hand.AimForward;
            hitPoint = origin;

            // Near-hand touch on an inventory slot first
            int count = Physics.OverlapSphereNonAlloc(origin, 0.22f, OverlapBuffer, UiLayerMask, QueryTriggerInteraction.Collide);
            GPButtonInventorySlot best = null;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider col = OverlapBuffer[i];
                if (col == null) continue;
                GPButtonInventorySlot candidate = col.GetComponent<GPButtonInventorySlot>();
                if (candidate == null) continue;
                Vector3 pt = GetClosestPointSafe(col, origin);
                float d = (pt - origin).sqrMagnitude;
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    best = candidate;
                    hitPoint = pt;
                }
            }
            if (best != null) return best;

            // Ray fallback into the inventory panel
            if (Physics.Raycast(new Ray(origin, forward), out RaycastHit hit, 5f, UiLayerMask, QueryTriggerInteraction.Collide))
            {
                GPButtonInventorySlot candidate = hit.collider.GetComponent<GPButtonInventorySlot>();
                if (candidate != null)
                {
                    hitPoint = hit.point;
                    return candidate;
                }
            }
            return null;
        }

        private void FindTarget(VrHandSlot slot, VrHandSlot otherSlot)
        {
            slot.ResetFrameTarget();

            PickupableItem held = slot.HeldItem;
            Vector3 origin = slot.Hand.WorldPosition;
            Vector3 forward = slot.Hand.AimForward;
            float nearRadius = Plugin.NearGrabRadiusConfig.Value;

            // Priority 0: Interactive child GoPointerButtons on the item held in your OTHER hand (e.g. Radio buttons while holding the Radio).
            PickupableItem otherHeld = otherSlot != null ? otherSlot.HeldItem : null;
            if (otherHeld != null)
            {
                // Reuse one list so holding an item doesn't allocate garbage every frame.
                otherHeld.GetComponentsInChildren(false, ChildButtonBuffer);
                List<GoPointerButton> childButtons = ChildButtonBuffer;
                GoPointerButton bestChild = null;
                float bestChildDistSq = float.MaxValue;
                Vector3 bestChildPt = origin;
                Transform bestChildTransform = null;
                float childNearRadiusSq = (nearRadius + 0.04f) * (nearRadius + 0.04f);

                for (int i = 0; i < childButtons.Count; i++)
                {
                    GoPointerButton btn = childButtons[i];
                    if (btn == null || btn == otherHeld || btn.unclickable || btn is PickupableItem) continue;
                    Collider col = btn.GetComponent<Collider>();
                    Vector3 pt = col != null ? GetClosestPointSafe(col, origin) : btn.transform.position;
                    float dSq = (pt - origin).sqrMagnitude;
                    if (dSq <= childNearRadiusSq && dSq < bestChildDistSq)
                    {
                        bestChildDistSq = dSq;
                        bestChild = btn;
                        bestChildPt = pt;
                        bestChildTransform = btn.transform;
                    }
                }

                if (bestChild != null)
                {
                    slot.HoveredButton = bestChild;
                    slot.IsNearTarget = true;
                    slot.HasRayHit = false;
                    slot.RayHitPoint = bestChildPt;
                    slot.RayHitTransform = bestChildTransform;
                    slot.RayHitLocalPoint = bestChildTransform != null ? bestChildTransform.InverseTransformPoint(bestChildPt) : bestChildPt;
                    return;
                }
            }

            // Priority 1: Near-hand proximity grab sphere (prioritizing small child buttons over enclosing PickupableItem bodies).
            int count = Physics.OverlapSphereNonAlloc(origin, nearRadius, OverlapBuffer, InteractionLayerMask, QueryTriggerInteraction.Collide);
            GoPointerButton bestChildNear = null;
            float bestChildNearDistSq = float.MaxValue;
            Vector3 bestChildNearPt = origin;

            GoPointerButton bestNear = null;
            Collider bestNearCol = null;
            float bestDistSq = float.MaxValue;
            Vector3 bestPoint = origin;

            for (int i = 0; i < count; i++)
            {
                Collider col = OverlapBuffer[i];
                if (col == null) continue;
                GoPointerButton candidate = ResolveEligibleButton(col, held);
                if (candidate == null) continue;

                Vector3 closest = GetClosestPointSafe(col, origin);
                float distSq = (closest - origin).sqrMagnitude;
                bool isChildControl = !(candidate is PickupableItem) && candidate.transform.parent != null
                    && candidate.transform.parent.GetComponentInParent<PickupableItem>() != null;

                if (isChildControl)
                {
                    if (distSq < bestChildNearDistSq)
                    {
                        bestChildNearDistSq = distSq;
                        bestChildNear = candidate;
                        bestChildNearPt = closest;
                    }
                }
                else if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestNear = candidate;
                    bestNearCol = col;
                    bestPoint = closest;
                }
            }

            if (bestChildNear != null)
            {
                slot.HoveredButton = bestChildNear;
                slot.IsNearTarget = true;
                slot.HasRayHit = false;
                slot.RayHitPoint = bestChildNearPt;
                return;
            }

            if (bestNear != null)
            {
                slot.HoveredButton = bestNear;
                slot.IsNearTarget = true;
                slot.HasRayHit = false;
                slot.RayHitPoint = bestPoint;

                // Holding an item right over a placing surface: find the actual surface point below or ahead of the hand
                // so the item can be previewed and set down there, not just dropped from the hand.
                if (held != null && bestNear.allowPlacingItems && bestNearCol != null
                    && (IsTopSurface(bestNearCol.Raycast(new Ray(origin + Vector3.up * 0.3f, Vector3.down), out RaycastHit surfaceHit, 0.6f), surfaceHit, origin)
                        || IsTopSurface(bestNearCol.Raycast(new Ray(origin, forward), out surfaceHit, nearRadius + 0.3f), surfaceHit, origin)))
                {
                    slot.HasRayHit = true;
                    slot.RayHitPoint = surfaceHit.point;
                    slot.RayHitTransform = surfaceHit.transform;
                    slot.RayHitLocalPoint = surfaceHit.transform != null ? surfaceHit.transform.InverseTransformPoint(surfaceHit.point) : surfaceHit.point;
                    slot.LastRaycastHit = surfaceHit;
                }
                return;
            }

            // Priority 2: Short controller ray fallback.
            float rayDist = Plugin.RayFallbackDistanceConfig.Value;
            if (Physics.Raycast(new Ray(origin, forward), out RaycastHit hit, rayDist, InteractionLayerMask, QueryTriggerInteraction.Collide))
            {
                GoPointerButton candidate = ResolveEligibleButton(hit.collider, held);
                if (candidate != null)
                {
                    slot.HoveredButton = candidate;
                    slot.IsNearTarget = false;
                    slot.HasRayHit = true;
                    slot.RayHitPoint = hit.point;
                    slot.RayHitTransform = hit.transform;
                    slot.RayHitLocalPoint = hit.transform != null ? hit.transform.InverseTransformPoint(hit.point) : hit.point;
                    slot.LastRaycastHit = hit;
                }
            }
        }

        // Only an upward-facing spot at or below the hand counts, so a shelf above the hand or a table's side never takes the item.
        private static bool IsTopSurface(bool didHit, RaycastHit hit, Vector3 handPosition)
        {
            return didHit && hit.normal.y > 0.5f && hit.point.y <= handPosition.y + 0.05f;
        }

        private static Vector3 GetClosestPointSafe(Collider col, Vector3 point)
        {
            if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mc && mc.convex))
            {
                return col.ClosestPoint(point);
            }
            return col.bounds.ClosestPoint(point);
        }

        private static GoPointerButton ResolveEligibleButton(Collider col, PickupableItem heldItem)
        {
            if (col == null) return null;
            if (col.CompareTag("ItemSubcollider") && col.transform.parent != null)
            {
                Collider parentCol = col.transform.parent.GetComponent<Collider>();
                if (parentCol != null) col = parentCol;
            }

            GoPointerButton button = col.GetComponent<GoPointerButton>();
            if (button == null || button.unclickable) return null;

            ShipItem shipItem = button.GetComponent<ShipItem>();
            if (heldItem != null && shipItem == heldItem)
            {
                return null;
            }

            if (heldItem == null && shipItem != null && shipItem.nailed)
            {
                if (shipItem is ShipItemCrate || shipItem is ShipItemBottle || shipItem is ShipItemBed)
                {
                    return button;
                }
                return null;
            }

            if (heldItem != null)
            {
                if (button is CrateInventoryButton crateBtn)
                {
                    ShipItem heldShipItem = heldItem as ShipItem;
                    return (GetCrateButtonItem(crateBtn) == null && heldShipItem != null && !heldShipItem.big && heldShipItem.sold)
                        ? button
                        : null;
                }

                // Native GoPointer.DoRaycast rule while holding an item: the held item's AllowOnItemClick decides
                // (hammer nailing, knife slicing, lamp hooks, placing surfaces). Otherwise other items and beds are
                // skipped, so a cup sitting on a table never wins over the table, while ordinary buttons stay usable.
                if (heldItem.AllowOnItemClick(button)) return button;
                if (shipItem != null || button.GetComponent<GPButtonBed>() != null) return null;
                return button;
            }

            return button;
        }

        private void HandleGrip(VrHandSlot slot, VrHandSlot otherSlot)
        {
            if (slot.Pointer == null) return;

            PickupableItem held = slot.HeldItem;

            if (held != null)
            {
                if (ShouldReleaseHeldItem(slot, held))
                {
                    if (slot.HoveredButton is CrateInventoryButton crateDrop && GetCrateButtonItem(crateDrop) == null
                        && held is ShipItem heldShip && !heldShip.big && heldShip.sold)
                    {
                        crateDrop.OnActivate(slot.Pointer);
                        slot.Hand.SendHaptic(0.35f, 0.04f);
                    }
                    else if (slot.IsPlacing)
                    {
                        SetDownInPlace(slot, held);
                    }
                    else
                    {
                        DropAndMaybeThrow(slot, held);
                    }
                }
                return;
            }

            if (!slot.Hand.GripDown)
            {
                return;
            }

            // Direct hand-to-hand transfer (works even though held items disable their colliders).
            PickupableItem otherHeld = otherSlot != null ? otherSlot.HeldItem : null;
            if (otherHeld != null && otherSlot.Pointer != null)
            {
                float handDist = Vector3.Distance(slot.Hand.WorldPosition, otherSlot.Hand.WorldPosition);
                float itemDist = Vector3.Distance(slot.Hand.WorldPosition, otherHeld.transform.position);
                float swapRadius = Mathf.Max(Plugin.NearGrabRadiusConfig.Value + 0.10f, 0.26f);
                if (handDist <= swapRadius || itemDist <= swapRadius)
                {
                    otherSlot.Pointer.DropItem();
                    slot.Pointer.PickUpItem(otherHeld);
                    slot.Hand.SendHaptic(0.35f, 0.04f);
                    otherSlot.Hand.SendHaptic(0.20f, 0.025f);
                    return;
                }
            }

            if (slot.HoveredButton == null)
            {
                return;
            }

            // Crate inventory slot? Pull the item directly into your hand on Grip.
            if (slot.HoveredButton is CrateInventoryButton crateGrab && GetCrateButtonItem(crateGrab) != null)
            {
                crateGrab.OnActivate(slot.Pointer);
                slot.Hand.SendHaptic(0.35f, 0.04f);
                return;
            }

            // Rotary control? (winch, steering wheel, bilge pump)
            if (VrRotaryController.IsRotaryControl(slot.HoveredButton))
            {
                slot.ActiveRotary = VrRotaryController.BeginGrab(slot.HoveredButton, slot.Hand);
                return;
            }

            // PickupableItem? (including hand-to-hand transfer)
            PickupableItem pickup = slot.HoveredButton.GetComponent<PickupableItem>();
            if (pickup != null)
            {
                ShipItem shipItem = pickup as ShipItem;
                if (shipItem != null && shipItem.nailed)
                {
                    return;
                }

                if (pickup.held != null && otherSlot.Pointer == pickup.held)
                {
                    otherSlot.Pointer.DropItem();
                }

                slot.Pointer.PickUpItem(pickup);
                slot.Hand.SendHaptic(0.35f, 0.04f);
            }
        }

        private void HandleTrigger(VrHandSlot slot)
        {
            if (slot.Pointer == null) return;

            // If a sticky-clicked control (e.g. RadioVolumeKnob) is currently engaged, allow releasing it
            // cleanly on a second Trigger press, Grip press, or walking out of reach.
            if (slot.ActiveClickedButton != null && slot.ActiveClickedButton.IsStickyClicked())
            {
                bool releaseSticky = slot.Hand.TriggerDown || slot.Hand.GripDown
                    || Vector3.Distance(slot.Hand.WorldPosition, slot.ActiveClickedButton.transform.position) > MaxRotaryGrabDistance;
                if (releaseSticky)
                {
                    ReleaseClickedButtonIfActive(slot);
                    if (UISoundPlayer.instance != null)
                    {
                        UISoundPlayer.instance.PlayUISound(UISounds.winchClick, 0.28f, 0.78f);
                    }
                    slot.Hand.SendHaptic(0.2f, 0.025f);
                }
                return;
            }

            PickupableItem held = slot.HeldItem;

            if (slot.Hand.TriggerDown)
            {
                if (held != null)
                {
                    if (slot.HoveredButton != null && !GameState.wasInSettingsMenu)
                    {
                        if (slot.HoveredButton is CrateInventoryButton crateBtn && GetCrateButtonItem(crateBtn) == null
                            && held is ShipItem heldShip && !heldShip.big && heldShip.sold)
                        {
                            crateBtn.OnActivate(slot.Pointer);
                            slot.Hand.SendHaptic(0.3f, 0.035f);
                            return;
                        }

                        if (slot.HoveredButton.OnItemClick(held))
                        {
                            held.OnDrop();
                            slot.Pointer.DropItem();
                            slot.Hand.SendHaptic(0.3f, 0.035f);
                            return;
                        }
                    }

                    held.OnAltActivate();
                    held.OnAltActivate(slot.Pointer);
                    slot.Hand.SendHaptic(0.25f, 0.03f);
                }
                else if (slot.HoveredButton != null)
                {
                    PickupableItem pickup = slot.HoveredButton.GetComponent<PickupableItem>();
                    if (pickup != null)
                    {
                        ShipItem shipItem = pickup as ShipItem;
                        if (shipItem != null && shipItem.big && CargoStorageUI.loadingCargoMode && shipItem.sold && !shipItem.nailed
                            && CargoStorageUI.instance != null && CargoStorageUI.instance.currentCarrier != null)
                        {
                            CargoStorageUI.instance.currentCarrier.InsertItem(shipItem);
                        }
                        else
                        {
                            slot.HoveredButton.OnAltActivate();
                            slot.HoveredButton.OnAltActivate(slot.Pointer);
                        }
                        slot.Hand.SendHaptic(0.28f, 0.03f);
                    }
                    else if (!VrRotaryController.IsRotaryControl(slot.HoveredButton) && !(slot.HoveredButton is CrateInventoryButton))
                    {
                        slot.ActiveClickedButton = slot.HoveredButton;
                        if (slot.Pointer.movement != null)
                        {
                            slot.ActiveClickedButton.Click(slot.Pointer.movement);
                        }
                        slot.ActiveClickedButton.OnActivate();
                        slot.ActiveClickedButton.OnActivate(slot.Pointer);
                        if (slot.HasRayHit)
                        {
                            slot.ActiveClickedButton.OnActivateHit(slot.LastRaycastHit);
                        }
                        if (slot.ActiveClickedButton.IsStickyClicked() && UISoundPlayer.instance != null)
                        {
                            UISoundPlayer.instance.PlayUISound(UISounds.winchClick, 0.33f, 0.85f);
                        }
                        slot.Hand.SendHaptic(0.3f, 0.035f);
                    }
                }
            }

            if (slot.Hand.TriggerPressed)
            {
                if (held != null)
                {
                    held.OnAltHeld();
                    held.OnAltHeld(slot.Pointer);
                }
                else if (slot.HoveredButton != null && slot.HoveredButton.GetComponent<PickupableItem>() != null)
                {
                    slot.HoveredButton.OnAltHeld();
                    slot.HoveredButton.OnAltHeld(slot.Pointer);
                }
            }

            if (slot.Hand.TriggerUp)
            {
                if (slot.ActiveClickedButton != null && !slot.ActiveClickedButton.IsStickyClicked())
                {
                    ReleaseClickedButtonIfActive(slot);
                }
            }
        }

        private static bool ShouldReleaseHeldItem(VrHandSlot slot, PickupableItem held)
        {
            GripMode mode = Plugin.GripModeConfig.Value;
            bool useToggle = mode == GripMode.ToggleAll || (mode == GripMode.ToggleBigItemsOnly && held != null && held.big);
            return useToggle ? slot.Hand.GripDown : slot.Hand.GripUp;
        }

        private static bool ShouldReleaseRotary(VrHandSlot slot)
        {
            return Plugin.GripModeConfig.Value == GripMode.ToggleAll ? slot.Hand.GripDown : !slot.Hand.GripPressed;
        }

        // Release at the previewed spot with no throw velocity, like native placement.
        private static void SetDownInPlace(VrHandSlot slot, PickupableItem held)
        {
            held.OnDrop();
            slot.Pointer.DropItem();
            slot.IsPlacing = false;
            slot.Hand.SendHaptic(0.3f, 0.035f);
        }

        private void DropAndMaybeThrow(VrHandSlot slot, PickupableItem held)
        {
            Vector3 releaseVel = slot.Hand.WorldVelocity * Plugin.ThrowVelocityMultiplierConfig.Value;
            ShipItem shipItem = held.GetComponent<ShipItem>();
            Rigidbody targetRb = null;
            if (shipItem != null && shipItem.GetItemRigidbody() != null)
            {
                targetRb = shipItem.GetItemRigidbody().GetComponent<Rigidbody>();
                // ItemRigidbody simulates in the unrotated walkCol frame while on a boat.
                if (shipItem.currentActualBoat != null)
                {
                    releaseVel = shipItem.currentActualBoat.InverseTransformDirection(releaseVel);
                }
            }

            held.OnDrop();
            slot.Pointer.DropItem();
            slot.Hand.SendHaptic(0.18f, 0.025f);

            if (targetRb != null && releaseVel.magnitude >= ThrowSpeedThreshold && _coroutineHost != null)
            {
                _coroutineHost.StartCoroutine(ApplyThrowVelocityNextFixedUpdate(targetRb, releaseVel));
            }
        }

        private static IEnumerator ApplyThrowVelocityNextFixedUpdate(Rigidbody rb, Vector3 velocity)
        {
            yield return new WaitForFixedUpdate();
            if (rb != null && !rb.isKinematic)
            {
                rb.velocity = Vector3.ClampMagnitude(velocity, 18f);
            }
        }

        private static void ReleaseRotaryIfActive(VrHandSlot slot)
        {
            if (slot.ActiveRotary != null)
            {
                VrRotaryController.EndGrab(slot.ActiveRotary);
                slot.ActiveRotary = null;
            }
        }

        private static void ReleaseClickedButtonIfActive(VrHandSlot slot)
        {
            if (slot.ActiveClickedButton != null)
            {
                if (slot.ActiveClickedButton.IsStickyClicked())
                {
                    slot.ActiveClickedButton.UnStickyClick();
                }
                slot.ActiveClickedButton.OnUnactivate();
                if (slot.Pointer != null)
                {
                    slot.ActiveClickedButton.OnUnactivate(slot.Pointer);
                }
                slot.ActiveClickedButton.Unclick();
                slot.ActiveClickedButton = null;
            }
        }

        private void UpdateLookUi()
        {
            if (_lookUi == null) return;

            if (!Plugin.ShowHoverTooltipsConfig.Value || !GameState.playing || GameState.inCursorMenu)
            {
                _lookUi.ClearText();
                return;
            }

            VrHandSlot displaySlot = null;
            if (Right.HoveredButton != null && Right.ActiveRotary == null)
            {
                displaySlot = Right;
            }
            else if (Left.HoveredButton != null && Left.ActiveRotary == null)
            {
                displaySlot = Left;
            }
            else if (Right.HeldItem != null)
            {
                displaySlot = Right;
            }
            else if (Left.HeldItem != null)
            {
                displaySlot = Left;
            }

            if (displaySlot == null || displaySlot.Pointer == null)
            {
                _lookUi.ClearText();
                return;
            }

            _lookUi.RegisterPointer(displaySlot.Pointer);
            if (displaySlot.HoveredButton != null)
            {
                _lookUi.ShowLookText(displaySlot.HoveredButton);
            }
            else if (displaySlot.HeldItem != null)
            {
                _lookUi.ShowHoldText(displaySlot.HeldItem);
            }
            else
            {
                _lookUi.ClearText();
            }
        }
    }

    internal static class VrHandPatches
    {
        [HarmonyPatch(typeof(GoPointer), "FixedUpdate")]
        [HarmonyPrefix]
        private static bool GoPointerFixedUpdatePrefix(GoPointer __instance)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig != null && rig.IsVrActive && rig.HandInteractor.IsOwnedPointer(__instance))
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(GoPointer), "LateUpdate")]
        [HarmonyPrefix]
        private static bool GoPointerLateUpdatePrefix(GoPointer __instance)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig != null && rig.IsVrActive && rig.HandInteractor.IsOwnedPointer(__instance))
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ShipItemBottle), nameof(ShipItemBottle.ExtraLateUpdate))]
        [HarmonyPrefix]
        private static bool BottleExtraLateUpdatePrefix(ShipItemBottle __instance)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig != null && rig.IsVrActive && rig.HandInteractor.IsVrHeldItem(__instance))
            {
                return false;
            }
            return true;
        }

        private static readonly FieldInfo SpyglassCamField = AccessTools.Field(typeof(ShipItemSpyglass), "cam");
        private static readonly FieldInfo SpyglassMinZoomField = AccessTools.Field(typeof(ShipItemSpyglass), "minZoom");
        private static readonly FieldInfo SpyglassMaxZoomField = AccessTools.Field(typeof(ShipItemSpyglass), "maxZoom");
        private static readonly FieldInfo SpyglassCurrentZoomField = AccessTools.Field(typeof(ShipItemSpyglass), "currentZoom");

        [HarmonyPatch(typeof(ShipItemSpyglass), nameof(ShipItemSpyglass.OnPickup))]
        [HarmonyPostfix]
        private static void SpyglassOnPickupPostfix(ShipItemSpyglass __instance)
        {
            FixSpyglassCamera(__instance, true);
        }

        [HarmonyPatch(typeof(ShipItemSpyglass), "UpdateCam")]
        [HarmonyPostfix]
        private static void SpyglassUpdateCamPostfix(ShipItemSpyglass __instance)
        {
            FixSpyglassCamera(__instance, false);
        }

        private static void FixSpyglassCamera(ShipItemSpyglass spyglass, bool logState)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive || spyglass == null) return;

            if (SpyglassCamField?.GetValue(spyglass) is Camera spyCam)
            {
                if (spyCam.stereoTargetEye != StereoTargetEyeMask.None)
                {
                    spyCam.stereoTargetEye = StereoTargetEyeMask.None;
                }
                UnityEngine.XR.XRDevice.DisableAutoXRCameraTracking(spyCam, true);

                // Explicitly set a custom Perspective projectionMatrix so Unity 2019 XR cannot override
                // the RenderTexture scope camera's zoomed FOV with the headset's ~100 deg stereo projection.
                float aspect = spyCam.targetTexture != null && spyCam.targetTexture.height > 0
                    ? (float)spyCam.targetTexture.width / spyCam.targetTexture.height
                    : 1f;
                float fov = Mathf.Clamp(spyCam.fieldOfView, 1f, 120f);
                spyCam.projectionMatrix = Matrix4x4.Perspective(fov, aspect, spyCam.nearClipPlane, spyCam.farClipPlane);

                if (logState)
                {
                    float minZ = SpyglassMinZoomField != null ? (float)SpyglassMinZoomField.GetValue(spyglass) : -1f;
                    float maxZ = SpyglassMaxZoomField != null ? (float)SpyglassMaxZoomField.GetValue(spyglass) : -1f;
                    float curZ = SpyglassCurrentZoomField != null ? (float)SpyglassCurrentZoomField.GetValue(spyglass) : -1f;
                    Plugin.Log.LogInfo($"[VR] Spyglass cam: enabled={spyCam.enabled}, fov={fov:F1} (min={minZ:F1}, max={maxZ:F1}, cur={curZ:F2}), aspect={aspect:F2}, hasRT={spyCam.targetTexture != null}.");
                }
            }
        }

        [HarmonyPatch(typeof(ShipItemFood), nameof(ShipItemFood.ExtraLateUpdate))]
        [HarmonyPrefix]
        private static bool FoodExtraLateUpdatePrefix(ShipItemFood __instance)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig != null && rig.IsVrActive && rig.HandInteractor.IsVrHeldItem(__instance))
            {
                return false;
            }
            return true;
        }
    }
}
