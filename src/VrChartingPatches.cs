using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HelmVR
{
    /// <summary>
    /// Enables 1:1 VR tabletop charting on Sailwind's MapTableCamera:
    /// keeps the player's normal stereo VR camera active (no disorienting top-down camera hijack)
    /// and routes the active VR controller laser directly into MapChart and ChartKitButton.
    /// </summary>
    internal static class VrChartingPatches
    {
        private const int ChartLayerMask = 8388608; // Layer 23: Map/Chart
        private const int DefaultLayerMask = 1;     // Layer 0: ChartKitButton
        private const float MaxChartRayDistance = 8f;

        private static readonly FieldInfo CurrentMapField = AccessTools.Field(typeof(MapTableCamera), "currentMap");
        private static readonly FieldInfo CamField = AccessTools.Field(typeof(MapTableCamera), "cam");
        private static readonly FieldInfo CamActivationTimerField = AccessTools.Field(typeof(MapTableCamera), "camActivationTimer");
        private static readonly FieldInfo ControllerPointerField = AccessTools.Field(typeof(MapTableCamera), "controllerPointer");
        private static readonly FieldInfo LooklightField = AccessTools.Field(typeof(MapTableCamera), "looklight");
        private static readonly MethodInfo UpdateQuillMethod = AccessTools.Method(typeof(MapTableCamera), "UpdateQuill");
        private static readonly MethodInfo UpdateProtMethod = AccessTools.Method(typeof(MapTableCamera), "UpdateProt");

        private static ChartKitButton _lastHoveredKitBtn;

        internal static bool IsChartingActive()
        {
            if (MapTableCamera.instance == null || CurrentMapField == null) return false;
            return (CurrentMapField.GetValue(MapTableCamera.instance) as ShipItem) != null;
        }

        [HarmonyPatch(typeof(MapTableCamera), nameof(MapTableCamera.EnableMapCam))]
        [HarmonyPostfix]
        private static void EnableMapCamPostfix(MapTableCamera __instance)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive || !Plugin.EnableVrChartingConfig.Value) return;

            // Keep the player's stereo VR camera in control so you look naturally at the physical chart.
            if (CamField?.GetValue(__instance) is Camera mapCam && mapCam.enabled)
            {
                mapCam.enabled = false;
            }

            if (ControllerPointerField?.GetValue(__instance) is Transform cp && cp.gameObject.activeSelf)
            {
                cp.gameObject.SetActive(false);
            }

            _lastHoveredKitBtn = null;
            Plugin.Log.LogInfo("[VR] Tabletop charting active (Trigger to plot/select tool, B/Y to exit).");
        }

        [HarmonyPatch(typeof(MapTableCamera), nameof(MapTableCamera.DisableMapCam))]
        [HarmonyPostfix]
        private static void DisableMapCamPostfix()
        {
            _lastHoveredKitBtn = null;
            MenuPointerPatches.ResetVisualState();
        }

        [HarmonyPatch(typeof(MapTableCamera), "LookRaycast")]
        [HarmonyPrefix]
        private static bool LookRaycastPrefix(MapTableCamera __instance)
        {
            VrRigController rig = VrRigController.Instance;
            if (rig == null || !rig.IsVrActive || !Plugin.EnableVrChartingConfig.Value)
            {
                return true;
            }

            if (CamField?.GetValue(__instance) is Camera mapCam && mapCam.enabled)
            {
                mapCam.enabled = false;
            }

            if (ControllerPointerField?.GetValue(__instance) is Transform cp && cp.gameObject.activeSelf)
            {
                cp.gameObject.SetActive(false);
            }

            float activationTimer = CamActivationTimerField != null
                ? (float)CamActivationTimerField.GetValue(__instance)
                : 0f;

            // Allow clean exit from charting with Right B or Left Y once activation cooldown finishes.
            if (activationTimer <= 0f && (rig.RightHand.TopFaceDown || rig.LeftHand.TopFaceDown))
            {
                __instance.DisableMapCam();
                return false;
            }

            if (!MenuPointerPatches.TryGetActiveHandRay(out Ray ray, out bool isLeft, out HandState activeHand, out _))
            {
                MenuPointerPatches.ResetVisualState();
                return false;
            }

            bool triggerReleased = activeHand.TriggerUp || Input.GetMouseButtonUp(0);
            bool hitAnything = false;

            // 1. Check ChartKitButton tools (quill, ruler, protractors, ink colors) on Layer 0 first so tools take priority over the chart beneath them.
            var looklight = LooklightField?.GetValue(__instance) as MeshFilter;
            ChartKitButton hoveredKit = null;

            if (Physics.Raycast(ray, out RaycastHit kitHit, MaxChartRayDistance, DefaultLayerMask))
            {
                hoveredKit = kitHit.collider.GetComponent<ChartKitButton>();
                if (hoveredKit != null)
                {
                    hitAnything = true;
                    MenuPointerPatches.SetInventoryHoverVisual(isLeft, kitHit.point, true, kitHit.transform);

                    if (hoveredKit != _lastHoveredKitBtn)
                    {
                        activeHand.SendHaptic(0.14f, 0.018f);
                        _lastHoveredKitBtn = hoveredKit;
                    }

                    if (looklight != null)
                    {
                        looklight.gameObject.SetActive(true);
                        looklight.transform.position = kitHit.transform.position;
                        looklight.transform.rotation = kitHit.transform.rotation;
                        looklight.transform.localScale = kitHit.transform.localScale * 1.01f;
                        MeshFilter btnMf = hoveredKit.GetComponent<MeshFilter>();
                        if (btnMf != null) looklight.sharedMesh = btnMf.sharedMesh;
                    }

                    if (triggerReleased && activationTimer <= 0f)
                    {
                        hoveredKit.OnActivate();
                        activeHand.SendHaptic(0.32f, 0.03f);
                        return false;
                    }
                }
            }

            if (hoveredKit == null)
            {
                _lastHoveredKitBtn = null;
                if (looklight != null && looklight.gameObject.activeSelf)
                {
                    looklight.gameObject.SetActive(false);
                }
            }

            // 2. Raycast onto the chart surface (Layer 23) for plotting with quill, ruler, and protractor.
            if (Physics.Raycast(ray, out RaycastHit chartHit, MaxChartRayDistance, ChartLayerMask))
            {
                if (!hitAnything)
                {
                    hitAnything = true;
                    MenuPointerPatches.SetInventoryHoverVisual(isLeft, chartHit.point, true, chartHit.transform);
                }

                MapChart mapChart = chartHit.collider.GetComponent<MapChart>();
                if (mapChart != null && hoveredKit == null)
                {
                    Vector3 localPoint = chartHit.collider.transform.InverseTransformPoint(chartHit.point);
                    if (triggerReleased && activationTimer <= 0f)
                    {
                        mapChart.OnActivate(localPoint);
                        activeHand.SendHaptic(0.28f, 0.025f);
                    }
                    else
                    {
                        mapChart.OnLook(localPoint);
                    }
                }

                UpdateQuillMethod?.Invoke(__instance, new object[] { chartHit.point });
                UpdateProtMethod?.Invoke(__instance, new object[] { chartHit.point });
            }

            if (!hitAnything)
            {
                MenuPointerPatches.ResetVisualState();
            }

            return false;
        }
    }
}
