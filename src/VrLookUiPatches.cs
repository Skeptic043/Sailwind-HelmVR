using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    /// <summary>
    /// Makes Sailwind's world-space LookUI billboard face the rendered VR eye
    /// and hides desktop mouse icons while in VR.
    /// </summary>
    internal static class VrLookUiPatches
    {
        private static readonly FieldInfo MouseLIconField = AccessTools.Field(typeof(LookUI), "mouseLIcon");
        private static readonly FieldInfo MouseRIconField = AccessTools.Field(typeof(LookUI), "mouseRIcon");
        private static readonly FieldInfo TextLIconField = AccessTools.Field(typeof(LookUI), "textLicon");
        private static readonly FieldInfo TextRIconField = AccessTools.Field(typeof(LookUI), "textRIcon");
        private static readonly FieldInfo CurrentButtonField = AccessTools.Field(typeof(LookUI), "currentButton");

        [HarmonyPatch(typeof(LookUI), nameof(LookUI.ShowLookText))]
        [HarmonyPostfix]
        private static void ShowLookTextPostfix(LookUI __instance)
        {
            ApplyVrBillboardAndIcons(__instance, false);
        }

        [HarmonyPatch(typeof(LookUI), nameof(LookUI.ShowHoldText))]
        [HarmonyPostfix]
        private static void ShowHoldTextPostfix(LookUI __instance)
        {
            ApplyVrBillboardAndIcons(__instance, true);
        }

        private static void ApplyVrBillboardAndIcons(LookUI lookUi, bool isHoldText)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive || lookUi == null) return;

            if (rig.EyeTransform != null)
            {
                // LookUI's TextMesh faces -Z (matches native LookAt(Camera.main.transform)).
                lookUi.transform.LookAt(rig.EyeWorldPosition);
            }

            var ml = MouseLIconField?.GetValue(lookUi) as Renderer;
            var mr = MouseRIconField?.GetValue(lookUi) as Renderer;
            bool hadL = ml != null && ml.enabled;
            bool hadR = mr != null && mr.enabled;

            if (ml != null) ml.enabled = false;
            if (mr != null) mr.enabled = false;

            bool showPrompts = Plugin.ShowVrActionPromptsConfig.Value;
            if (TextLIconField?.GetValue(lookUi) is TextMesh tl)
            {
                if (showPrompts && hadL)
                {
                    var btn = CurrentButtonField?.GetValue(lookUi) as GoPointerButton;
                    bool isGripAction = isHoldText
                        || (btn != null && (btn.GetComponent<PickupableItem>() != null || btn is CrateInventoryButton || VrRotaryController.IsRotaryControl(btn)));
                    tl.text = isGripAction ? "[Grip]" : "[Trig]";
                }
                else
                {
                    tl.text = string.Empty;
                }
            }

            if (TextRIconField?.GetValue(lookUi) is TextMesh tr)
            {
                tr.text = (showPrompts && hadR) ? "[Trig]" : string.Empty;
            }
        }
    }
}
