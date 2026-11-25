using System.Collections.Generic;
using PsychoticLab;
using UnityEngine;

namespace HelmVR
{
    /// <summary>
    /// Renders VR hands using Sailwind's own in-game Synty low-poly hand meshes once loaded
    /// (falling back to a lightweight flat-shaded mesh on the Start Menu), plus a thin
    /// 2.6 mm pointer beam and collider-anchored hit dots.
    /// </summary>
    internal sealed class HandVisuals
    {
        private const float MenuRayLength = 4.0f;
        private const float ManualAimRayLength = 2.2f;
        private const float RayThickness = 0.0026f;
        private const float HitDotDiameter = 0.011f;

        private sealed class HandRig
        {
            public GameObject Root;
            public GameObject FallbackGroup;
            public GameObject NativeHandGo;
            public Renderer NativeHandRenderer;
            public Vector3 NativeBaseScale = Vector3.one;
            public Vector3 NativeBaseLocalPos = Vector3.zero;
            public Transform IndexPivot;
            public Transform[] OtherFingerPivots;
            public Transform ThumbPivot;
            public Transform RayBar;
            public readonly List<Renderer> GloveRenderers = new List<Renderer>();
            public readonly List<Renderer> CuffRenderers = new List<Renderer>();
            public Renderer RayRenderer;
            public float SideSign;
        }

        private HandRig _left;
        private HandRig _right;
        private GameObject _leftHitDot;
        private Renderer _leftHitDotRenderer;
        private GameObject _rightHitDot;
        private Renderer _rightHitDotRenderer;
        private bool _loadedNativeHands;
        private float _nextNativeSearchTime;

        private Material _gloveIdleMat;
        private Material _gloveHoverMat;
        private Material _gloveGrabMat;
        private Material _cuffLeatherMat;
        private Material _cuffBrassMat;
        private Material _sleeveLinenMat;
        private Material _rayIdleMat;
        private Material _rayHoverMat;

        // Matte Sailwind-inspired nautical palette: warm weathered skin/sailor glove, dark leather wrist wrap, linen cuff, aged brass clasp.
        private static readonly Color GloveIdleColor = new Color(0.79f, 0.65f, 0.51f, 1f);
        private static readonly Color GloveHoverColor = new Color(0.76f, 0.85f, 0.70f, 1f);
        private static readonly Color GloveGrabColor = new Color(0.84f, 0.70f, 0.48f, 1f);
        private static readonly Color CuffLeatherColor = new Color(0.25f, 0.16f, 0.10f, 1f);
        private static readonly Color CuffBrassColor = new Color(0.68f, 0.53f, 0.24f, 1f);
        private static readonly Color SleeveLinenColor = new Color(0.84f, 0.79f, 0.69f, 1f);
        private static readonly Color RayIdleColor = new Color(0.95f, 0.90f, 0.76f, 1f);
        private static readonly Color RayHoverColor = new Color(0.32f, 0.95f, 0.48f, 1f);

        public void Update(VrHandSlot leftSlot, VrHandSlot rightSlot, bool showHands, bool inMenu, bool holdAimLaser)
        {
            EnsureCreated();
            if (!_loadedNativeHands && Time.unscaledTime >= _nextNativeSearchTime)
            {
                _nextNativeSearchTime = Time.unscaledTime + 1.5f;
                TryAttachNativeSailwindHands();
            }

            bool shouldShow = showHands || (inMenu && Plugin.EnableVrMenuPointerConfig.Value);
            if (!shouldShow)
            {
                SetVisible(false);
                return;
            }

            UpdateHand(_left, leftSlot, true, inMenu, holdAimLaser);
            UpdateHand(_right, rightSlot, false, inMenu, holdAimLaser);

            UpdateHitDot(leftSlot, rightSlot, inMenu);
        }

        public void Destroy()
        {
            if (_left?.Root != null) Object.Destroy(_left.Root);
            if (_right?.Root != null) Object.Destroy(_right.Root);
            if (_leftHitDot != null) Object.Destroy(_leftHitDot);
            if (_rightHitDot != null) Object.Destroy(_rightHitDot);
            _left = null;
            _right = null;
            _leftHitDot = null;
            _leftHitDotRenderer = null;
            _rightHitDot = null;
            _rightHitDotRenderer = null;
        }

        private void EnsureCreated()
        {
            if (_gloveIdleMat == null)
            {
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Material baseMat = tmp.GetComponent<Renderer>().sharedMaterial;
                _gloveIdleMat = MakeNauticalMaterial(baseMat, GloveIdleColor, 0.14f, 0.05f);
                _gloveHoverMat = MakeNauticalMaterial(baseMat, GloveHoverColor, 0.22f, 0.06f);
                _gloveGrabMat = MakeNauticalMaterial(baseMat, GloveGrabColor, 0.20f, 0.08f);
                _cuffLeatherMat = MakeNauticalMaterial(baseMat, CuffLeatherColor, 0.06f, 0.08f);
                _cuffBrassMat = MakeNauticalMaterial(baseMat, CuffBrassColor, 0.14f, 0.40f);
                _sleeveLinenMat = MakeNauticalMaterial(baseMat, SleeveLinenColor, 0.10f, 0.04f);
                _rayIdleMat = MakeNauticalMaterial(baseMat, RayIdleColor, 0.75f, 0.10f);
                _rayHoverMat = MakeNauticalMaterial(baseMat, RayHoverColor, 0.85f, 0.10f);
                Object.Destroy(tmp);
            }

            if (_left == null || _left.Root == null)
            {
                _left = BuildHandRig("HelmVR_LeftHand", true);
            }
            if (_right == null || _right.Root == null)
            {
                _right = BuildHandRig("HelmVR_RightHand", false);
            }
            if (_leftHitDot == null)
            {
                _leftHitDot = BuildHitDot("HelmVR_LeftHitDot", out _leftHitDotRenderer);
            }
            if (_rightHitDot == null)
            {
                _rightHitDot = BuildHitDot("HelmVR_RightHitDot", out _rightHitDotRenderer);
            }
        }

        private GameObject BuildHitDot(string name, out Renderer dotRenderer)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = name;
            dot.layer = 5;
            Object.DontDestroyOnLoad(dot);
            StripCollider(dot);
            dot.transform.localScale = Vector3.one * HitDotDiameter;
            dotRenderer = ConfigureRenderer(dot, _rayHoverMat);
            dot.SetActive(false);
            return dot;
        }

        private static Material MakeNauticalMaterial(Material template, Color color, float emissionStrength, float smoothness)
        {
            var mat = new Material(template);
            mat.color = color;
            mat.SetFloat("_Glossiness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * emissionStrength);
            return mat;
        }

        private void SetVisible(bool visible)
        {
            if (_left?.Root != null && _left.Root.activeSelf != visible) _left.Root.SetActive(visible);
            if (_right?.Root != null && _right.Root.activeSelf != visible) _right.Root.SetActive(visible);
            if (_leftHitDot != null && _leftHitDot.activeSelf != visible) _leftHitDot.SetActive(visible);
            if (_rightHitDot != null && _rightHitDot.activeSelf != visible) _rightHitDot.SetActive(visible);
        }

        private void UpdateHand(HandRig rig, VrHandSlot slot, bool isLeft, bool inMenu, bool holdAimLaser)
        {
            if (rig == null || rig.Root == null || slot == null) return;

            bool show = slot.Hand.HasPose;
            if (rig.Root.activeSelf != show) rig.Root.SetActive(show);
            if (!show) return;

            rig.Root.transform.position = slot.Hand.WorldPosition;
            rig.Root.transform.rotation = slot.Hand.AimRotation;

            int handLayer = (inMenu && !VrChartingPatches.IsChartingActive()) ? 5 : 0;
            bool gripping = slot.Hand.GripPressed || slot.HeldItem != null || slot.ActiveRotary != null;

            if (rig.NativeHandGo != null && rig.NativeHandRenderer != null)
            {
                if (rig.NativeHandGo.layer != handLayer) rig.NativeHandGo.layer = handLayer;
                rig.NativeHandGo.transform.localPosition = rig.NativeBaseLocalPos + new Vector3(0f, Plugin.HandVerticalOffsetConfig.Value, 0f);
                rig.NativeHandGo.transform.localScale = gripping
                    ? Vector3.Scale(rig.NativeBaseScale, new Vector3(0.92f, 0.92f, 0.88f))
                    : rig.NativeBaseScale;
            }
            else
            {
                for (int i = 0; i < rig.GloveRenderers.Count; i++)
                {
                    if (rig.GloveRenderers[i].gameObject.layer != handLayer)
                        rig.GloveRenderers[i].gameObject.layer = handLayer;
                }
                for (int i = 0; i < rig.CuffRenderers.Count; i++)
                {
                    if (rig.CuffRenderers[i].gameObject.layer != handLayer)
                        rig.CuffRenderers[i].gameObject.layer = handLayer;
                }

                float curl = gripping ? 1f : Mathf.Clamp01(slot.Hand.Grip);
                bool pointing = !gripping && (inMenu || holdAimLaser || slot.HasRayHit);

                float indexAngle = pointing ? 6f : Mathf.Lerp(10f, 68f, curl);
                float otherAngle = pointing ? 42f : Mathf.Lerp(12f, 70f, curl);

                rig.IndexPivot.localEulerAngles = new Vector3(indexAngle, 0f, 0f);
                for (int i = 0; i < rig.OtherFingerPivots.Length; i++)
                {
                    rig.OtherFingerPivots[i].localEulerAngles = new Vector3(otherAngle + i * 3f, 0f, 0f);
                }
                rig.ThumbPivot.localEulerAngles = new Vector3(
                    Mathf.Lerp(10f, 34f, curl),
                    rig.SideSign * Mathf.Lerp(32f, 14f, curl),
                    0f);

                Material gloveMat = gripping ? _gloveGrabMat : (slot.IsNearTarget ? _gloveHoverMat : _gloveIdleMat);
                for (int i = 0; i < rig.GloveRenderers.Count; i++)
                {
                    rig.GloveRenderers[i].sharedMaterial = gloveMat;
                }
            }

            UpdateRayBar(rig, slot, isLeft, inMenu, holdAimLaser);
        }

        private void UpdateRayBar(HandRig rig, VrHandSlot slot, bool isLeft, bool inMenu, bool holdAimLaser)
        {
            Transform bar = rig.RayBar;
            if (bar == null) return;

            bool show = false;
            float len = MenuRayLength;
            Material mat = _rayIdleMat;
            int layer = 0;

            if (inMenu && Plugin.EnableVrMenuPointerConfig.Value)
            {
                bool isMenuHitHand = MenuPointerPatches.LastRayHitMenu && MenuPointerPatches.LastRayHitLeftHand == isLeft;
                bool isActiveMenuHand = MenuPointerPatches.IsLeftHandActive == isLeft || isMenuHitHand;
                if (isActiveMenuHand)
                {
                    show = true;
                    layer = VrChartingPatches.IsChartingActive() ? 0 : 5;
                    if (isMenuHitHand)
                    {
                        Vector3 hitPt = MenuPointerPatches.GetResolvedMenuHitPoint();
                        len = Mathf.Clamp(Vector3.Distance(slot.Hand.WorldPosition, hitPt), 0.05f, 15f);
                        if (MenuPointerPatches.LastRayHoveredClickable)
                        {
                            mat = _rayHoverMat;
                        }
                    }
                }
            }
            else if (!inMenu && slot.ActiveRotary == null)
            {
                layer = 0;
                if (Plugin.ShowGameplayAimRayConfig.Value && slot.HasRayHit)
                {
                    show = true;
                    Vector3 hitPt = slot.GetResolvedRayHitPoint();
                    len = Mathf.Clamp(Vector3.Distance(slot.Hand.WorldPosition, hitPt), 0.05f, 3f);
                    mat = _rayHoverMat;
                }
                else if (holdAimLaser)
                {
                    show = true;
                    len = ManualAimRayLength;
                    mat = _rayIdleMat;
                }
            }

            if (bar.gameObject.activeSelf != show) bar.gameObject.SetActive(show);
            if (!show) return;

            if (bar.gameObject.layer != layer) bar.gameObject.layer = layer;
            float startZ = 0.04f;
            float beamLen = Mathf.Max(0.02f, len - startZ);
            bar.localPosition = new Vector3(0f, 0f, startZ + beamLen * 0.5f);
            bar.localScale = new Vector3(RayThickness, RayThickness, beamLen);
            rig.RayRenderer.sharedMaterial = mat;
        }

        private void UpdateHitDot(VrHandSlot leftSlot, VrHandSlot rightSlot, bool inMenu)
        {
            if (inMenu)
            {
                if (MenuPointerPatches.LastRayHitMenu)
                {
                    int dotLayer = VrChartingPatches.IsChartingActive() ? 0 : 5;
                    bool isLeft = MenuPointerPatches.LastRayHitLeftHand;
                    Vector3 hitPt = MenuPointerPatches.GetResolvedMenuHitPoint();
                    Material dotMat = MenuPointerPatches.LastRayHoveredClickable ? _rayHoverMat : _rayIdleMat;
                    SetDot(_leftHitDot, _leftHitDotRenderer, isLeft, dotLayer, hitPt, dotMat);
                    SetDot(_rightHitDot, _rightHitDotRenderer, !isLeft, dotLayer, hitPt, dotMat);
                }
                else
                {
                    SetDot(_leftHitDot, _leftHitDotRenderer, false, 5, Vector3.zero, _rayHoverMat);
                    SetDot(_rightHitDot, _rightHitDotRenderer, false, 5, Vector3.zero, _rayHoverMat);
                }
                return;
            }

            bool leftHit = leftSlot != null && leftSlot.HasRayHit;
            bool rightHit = rightSlot != null && rightSlot.HasRayHit;
            SetDot(_leftHitDot, _leftHitDotRenderer, leftHit, 0, leftHit ? leftSlot.GetResolvedRayHitPoint() : Vector3.zero, _rayHoverMat);
            SetDot(_rightHitDot, _rightHitDotRenderer, rightHit, 0, rightHit ? rightSlot.GetResolvedRayHitPoint() : Vector3.zero, _rayHoverMat);
        }

        private static void SetDot(GameObject dot, Renderer dotRenderer, bool active, int layer, Vector3 pos, Material mat)
        {
            if (dot == null) return;
            if (dot.activeSelf != active) dot.SetActive(active);
            if (!active) return;
            if (dot.layer != layer) dot.layer = layer;
            dot.transform.position = pos;
            if (dotRenderer != null) dotRenderer.sharedMaterial = mat;
        }

        private HandRig BuildHandRig(string name, bool isLeft)
        {
            float side = isLeft ? 1f : -1f;
            var root = new GameObject(name);
            root.layer = 0;
            Object.DontDestroyOnLoad(root);

            var rig = new HandRig
            {
                Root = root,
                SideSign = side
            };

            var fallback = new GameObject("FallbackHand");
            fallback.transform.SetParent(root.transform, false);
            fallback.SetActive(false);
            rig.FallbackGroup = fallback;

            // Crunchy flat-shaded low-poly sailor hand (fallback until Sailwind's native Synty hand meshes load)
            GameObject palm = CreateTaperedPart("Palm", fallback.transform,
                new Vector3(0f, 0f, -0.046f), Vector3.zero,
                0.062f, 0.026f, 0.082f, 0.028f, 0.082f, _gloveIdleMat);
            rig.GloveRenderers.Add(palm.GetComponent<Renderer>());

            // Faceted dark leather wrist wrap + rolled sailcloth linen cuff
            GameObject cuff = CreateTaperedPart("WristLeather", fallback.transform,
                new Vector3(0f, -0.001f, -0.074f), Vector3.zero,
                0.068f, 0.034f, 0.064f, 0.030f, 0.030f, _cuffLeatherMat);
            rig.CuffRenderers.Add(cuff.GetComponent<Renderer>());

            GameObject sleeve = CreateTaperedPart("LinenCuff", fallback.transform,
                new Vector3(0f, -0.002f, -0.098f), Vector3.zero,
                0.076f, 0.042f, 0.070f, 0.036f, 0.026f, _sleeveLinenMat);
            rig.CuffRenderers.Add(sleeve.GetComponent<Renderer>());

            // Articulated flat-shaded tapered fingers
            rig.IndexPivot = BuildFinger(fallback.transform, "Index", new Vector3(side * 0.029f, 0.001f, 0.034f), 0.058f, 0.015f, 0.010f, rig.GloveRenderers);
            rig.OtherFingerPivots = new[]
            {
                BuildFinger(fallback.transform, "Middle", new Vector3(side * 0.010f, 0.002f, 0.034f), 0.064f, 0.015f, 0.010f, rig.GloveRenderers),
                BuildFinger(fallback.transform, "Ring", new Vector3(side * -0.010f, 0.001f, 0.034f), 0.059f, 0.014f, 0.0095f, rig.GloveRenderers),
                BuildFinger(fallback.transform, "Pinky", new Vector3(side * -0.029f, -0.001f, 0.032f), 0.047f, 0.013f, 0.0085f, rig.GloveRenderers)
            };

            // Tapered low-poly thumb
            rig.ThumbPivot = BuildFinger(fallback.transform, "Thumb", new Vector3(side * 0.036f, -0.004f, -0.006f), 0.050f, 0.017f, 0.011f, rig.GloveRenderers);
            rig.ThumbPivot.localEulerAngles = new Vector3(10f, side * 32f, 0f);

            // Thin pointer thread
            GameObject rayBar = CreatePart(PrimitiveType.Cube, "LaserBeam", root.transform,
                new Vector3(0f, 0f, MenuRayLength * 0.5f), Vector3.zero, new Vector3(RayThickness, RayThickness, MenuRayLength), _rayIdleMat);
            rayBar.layer = 5;
            rayBar.SetActive(false);
            rig.RayBar = rayBar.transform;
            rig.RayRenderer = rayBar.GetComponent<Renderer>();

            root.SetActive(false);
            return rig;
        }

        private Transform BuildFinger(Transform parent, string name, Vector3 pivotLocalPos, float length, float baseSize, float tipSize, List<Renderer> rendererList)
        {
            var pivot = new GameObject(name + "Pivot");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = pivotLocalPos;

            GameObject mesh = CreateTaperedPart(name, pivot.transform,
                Vector3.zero, Vector3.zero, baseSize, baseSize, tipSize, tipSize, length, _gloveIdleMat);
            rendererList.Add(mesh.GetComponent<Renderer>());
            return pivot.transform;
        }

        private static GameObject CreateTaperedPart(string name, Transform parent, Vector3 localPos, Vector3 localEuler,
            float backW, float backH, float frontW, float frontH, float length, Material mat)
        {
            var go = new GameObject(name);
            go.layer = 0;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildTaperedPrismMesh(backW, backH, frontW, frontH, length);

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static Mesh BuildTaperedPrismMesh(float backW, float backH, float frontW, float frontH, float length)
        {
            float bw = backW * 0.5f;
            float bh = backH * 0.5f;
            float fw = frontW * 0.5f;
            float fh = frontH * 0.5f;

            // Unshared per-face vertices so RecalculateNormals produces crisp flat-shaded low-poly facets.
            Vector3[] verts =
            {
                // Back (-Z)
                new Vector3(-bw, -bh, 0f), new Vector3(-bw,  bh, 0f), new Vector3( bw,  bh, 0f), new Vector3( bw, -bh, 0f),
                // Front (+Z)
                new Vector3( fw, -fh, length), new Vector3( fw,  fh, length), new Vector3(-fw,  fh, length), new Vector3(-fw, -fh, length),
                // Top (+Y)
                new Vector3(-bw,  bh, 0f), new Vector3(-fw,  fh, length), new Vector3( fw,  fh, length), new Vector3( bw,  bh, 0f),
                // Bottom (-Y)
                new Vector3( bw, -bh, 0f), new Vector3( fw, -fh, length), new Vector3(-fw, -fh, length), new Vector3(-bw, -bh, 0f),
                // Right (+X)
                new Vector3( bw, -bh, 0f), new Vector3( bw,  bh, 0f), new Vector3( fw,  fh, length), new Vector3( fw, -fh, length),
                // Left (-X)
                new Vector3(-bw,  bh, 0f), new Vector3(-bw, -bh, 0f), new Vector3(-fw, -fh, length), new Vector3(-fw,  fh, length)
            };

            int[] tris = new int[36];
            for (int f = 0; f < 6; f++)
            {
                int v = f * 4;
                int t = f * 6;
                tris[t] = v;
                tris[t + 1] = v + 1;
                tris[t + 2] = v + 2;
                tris[t + 3] = v;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;
            }

            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = 0;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;
            go.transform.localScale = localScale;
            ConfigureRenderer(go, mat);
            return go;
        }

        private void TryAttachNativeSailwindHands()
        {
            if (_left?.Root == null || _right?.Root == null) return;

            SkinnedMeshRenderer leftSrc = null;
            SkinnedMeshRenderer rightSrc = null;

            // 1. Check Sailwind's Synty CharacterCustomizer / CharacterRandomizer hand lists first.
            CharacterCustomizer[] customizers = Resources.FindObjectsOfTypeAll<CharacterCustomizer>();
            for (int i = 0; i < customizers.Length && (leftSrc == null || rightSrc == null); i++)
            {
                CharacterObjectGroups g = customizers[i]?.male;
                if (g == null) continue;
                if (leftSrc == null && g.hand_Left != null && g.hand_Left.Count > 0 && g.hand_Left[0] != null)
                    leftSrc = g.hand_Left[0].GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (rightSrc == null && g.hand_Right != null && g.hand_Right.Count > 0 && g.hand_Right[0] != null)
                    rightSrc = g.hand_Right[0].GetComponentInChildren<SkinnedMeshRenderer>(true);
            }

            // 2. Fallback: scan loaded SkinnedMeshRenderers for Synty Hand_Left / Hand_Right meshes.
            if (leftSrc == null || rightSrc == null)
            {
                SkinnedMeshRenderer[] allSmr = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();
                for (int i = 0; i < allSmr.Length && (leftSrc == null || rightSrc == null); i++)
                {
                    SkinnedMeshRenderer smr = allSmr[i];
                    if (smr == null || smr.sharedMesh == null) continue;
                    string n = (smr.name + " " + smr.sharedMesh.name).ToLowerInvariant();
                    if (leftSrc == null && (n.Contains("hand_left") || n.Contains("handleft"))) leftSrc = smr;
                    else if (rightSrc == null && (n.Contains("hand_right") || n.Contains("handright"))) rightSrc = smr;
                }
            }

            if (leftSrc == null || rightSrc == null) return;

            AttachNativeHandMesh(_left, leftSrc, true);
            AttachNativeHandMesh(_right, rightSrc, false);
            _loadedNativeHands = true;
            Plugin.Log.LogInfo($"[VR] Attached native Sailwind hand meshes: L='{leftSrc.sharedMesh.name}' R='{rightSrc.sharedMesh.name}'.");
        }

        private void AttachNativeHandMesh(HandRig rig, SkinnedMeshRenderer src, bool isLeft)
        {
            Mesh srcMesh = src.sharedMesh;
            if (srcMesh == null) return;

            // Normalize Synty's centimeter-scale GPU-only mesh (~23 units, isReadable=false in builds) via Transform scale & offset.
            Bounds b = srcMesh.bounds;
            float maxExtent = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float normScale = maxExtent > 0.001f ? (0.18f / maxExtent) : 1f;
            Quaternion rot = Quaternion.Euler(0f, isLeft ? 90f : -90f, 0f);

            var go = new GameObject(isLeft ? "NativeLeftHand" : "NativeRightHand");
            go.layer = 0;
            go.transform.SetParent(rig.Root.transform, false);
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one * normScale;
            go.transform.localPosition = -(rot * (b.center * normScale));

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = srcMesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = src.sharedMaterial != null ? new Material(src.sharedMaterial) : _gloveIdleMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            rig.NativeHandGo = go;
            rig.NativeHandRenderer = mr;
            rig.NativeBaseScale = Vector3.one * normScale;
            rig.NativeBaseLocalPos = -(rot * (b.center * normScale));
            if (rig.FallbackGroup != null) rig.FallbackGroup.SetActive(false);

            Plugin.Log.LogInfo($"[VR] Native {(isLeft ? "Left" : "Right")} hand scale={normScale:F4}, bounds={b.size.ToString("F3")}, mat='{(src.sharedMaterial != null ? src.sharedMaterial.name : "fallback")}'.");
        }

        private static Renderer ConfigureRenderer(GameObject go, Material mat)
        {
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static void StripCollider(GameObject go)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }
        }
    }
}
