using UnityEngine;

namespace HelmVR
{
    /// <summary>
    /// Renders a lightweight, stereo-safe peripheral comfort vignette around the player's eyes
    /// using an inverted 12-band dome with Oculus/Unlit Transparent Color (the same unlit shader
    /// proven to render reliably in Sailwind's Multi-Pass VR build).
    /// </summary>
    internal sealed class ComfortVignette
    {
        private const float DomeRadius = 0.38f;
        private const int Slices = 32;
        private const int BandCount = 12;
        private const float MinPolarDeg = 10f;
        private const float MaxPolarDeg = 72f;
        private const float FeatherWidth = 0.28f;
        private const float EaseInSpeed = 5.5f;
        private const float EaseOutSpeed = 3.5f;

        private GameObject _dome;
        private Mesh _mesh;
        private Material[] _bandMaterials;
        private float _currentStrength;
        private float _lastAppliedStrength = -1f;

        public void Update(Vector3 eyeWorldPosition, Quaternion eyeWorldRotation, float targetStrength, float unscaledDeltaTime)
        {
            targetStrength = Mathf.Clamp01(targetStrength);
            float speed = targetStrength > _currentStrength ? EaseInSpeed : EaseOutSpeed;
            _currentStrength = Mathf.MoveTowards(_currentStrength, targetStrength, speed * unscaledDeltaTime);

            if (_currentStrength <= 0.001f)
            {
                if (_dome != null && _dome.activeSelf)
                {
                    _dome.SetActive(false);
                }
                return;
            }

            EnsureCreated();
            if (_dome == null || _bandMaterials == null) return;

            if (!_dome.activeSelf)
            {
                _dome.SetActive(true);
            }

            _dome.transform.position = eyeWorldPosition;
            _dome.transform.rotation = eyeWorldRotation;

            // Skip updating all 12 band materials when vignette strength is steady (e.g. standing still or aboard ship).
            if (Mathf.Abs(_currentStrength - _lastAppliedStrength) <= 0.0005f) return;
            _lastAppliedStrength = _currentStrength;

            // Smoothstep alpha across the 12 concentric polar bands based on current vignette strength.
            // At 0.15 this trims the outer headset corners (~48 deg half-angle); at 1.0 it closes down to a ~14 deg tunnel.
            float innerU = Mathf.Lerp(0.72f, 0.06f, _currentStrength);
            float globalFade = Mathf.Clamp01(_currentStrength * 5f);

            for (int b = 0; b < BandCount; b++)
            {
                float bandU = (b + 0.5f) / BandCount;
                float t = Mathf.Clamp01((bandU - innerU) / FeatherWidth);
                float alpha = (t * t * (3f - 2f * t)) * globalFade;
                _bandMaterials[b].color = new Color(0f, 0f, 0f, alpha);
            }
        }

        public void HideImmediate()
        {
            _currentStrength = 0f;
            _lastAppliedStrength = -1f;
            if (_dome != null && _dome.activeSelf)
            {
                _dome.SetActive(false);
            }
        }

        public void Destroy()
        {
            _currentStrength = 0f;
            if (_dome != null)
            {
                Object.Destroy(_dome);
                _dome = null;
            }
            if (_mesh != null)
            {
                Object.Destroy(_mesh);
                _mesh = null;
            }
            if (_bandMaterials != null)
            {
                for (int i = 0; i < _bandMaterials.Length; i++)
                {
                    if (_bandMaterials[i] != null) Object.Destroy(_bandMaterials[i]);
                }
                _bandMaterials = null;
            }
        }

        private void EnsureCreated()
        {
            if (_dome != null) return;

            _mesh = BuildBandedInvertedDomeMesh(Slices, BandCount, DomeRadius, MinPolarDeg, MaxPolarDeg);
            Material template = BuildTemplateMaterial(out string shaderName);

            _bandMaterials = new Material[BandCount];
            for (int i = 0; i < BandCount; i++)
            {
                _bandMaterials[i] = new Material(template);
            }
            Object.Destroy(template);

            _dome = new GameObject("HelmVR_ComfortVignette");
            _dome.layer = 2; // Ignore Raycast
            Object.DontDestroyOnLoad(_dome);

            MeshFilter mf = _dome.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;

            MeshRenderer mr = _dome.AddComponent<MeshRenderer>();
            mr.sharedMaterials = _bandMaterials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            _dome.SetActive(false);
            Plugin.Log.LogInfo($"[VR] Initialized comfort vignette ({BandCount} bands, shader='{shaderName}').");
        }

        private static Material BuildTemplateMaterial(out string shaderName)
        {
            Shader oculusFade = Shader.Find("Oculus/Unlit Transparent Color");
            Material mat;
            if (oculusFade != null)
            {
                mat = new Material(oculusFade);
                shaderName = oculusFade.name;
            }
            else
            {
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mat = new Material(tmp.GetComponent<Renderer>().sharedMaterial);
                Object.Destroy(tmp);
                shaderName = mat.shader != null ? mat.shader.name : "Standard";
                mat.SetFloat("_Mode", 2f);
                mat.SetFloat("_Glossiness", 0f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_SpecularHighlights", 0f);
                mat.SetFloat("_GlossyReflections", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_ALPHABLEND_ON");
            }

            mat.color = new Color(0f, 0f, 0f, 0f);
            // Render over the 3D world, just below the sleep blackout dome (renderQueue 5000).
            mat.renderQueue = 4990;
            return mat;
        }

        private static Mesh BuildBandedInvertedDomeMesh(int slices, int bands, float radius, float minPolarDeg, float maxPolarDeg)
        {
            int vertCols = slices + 1;
            int vertRows = bands + 1;
            var verts = new Vector3[vertCols * vertRows];
            float minRad = minPolarDeg * Mathf.Deg2Rad;
            float maxRad = maxPolarDeg * Mathf.Deg2Rad;

            for (int r = 0; r < vertRows; r++)
            {
                float u = r / (float)bands;
                float theta = Mathf.Lerp(minRad, maxRad, u);
                float sinT = Mathf.Sin(theta);
                float cosT = Mathf.Cos(theta);

                for (int s = 0; s < vertCols; s++)
                {
                    float phi = (s / (float)slices) * Mathf.PI * 2f;
                    int idx = r * vertCols + s;
                    verts[idx] = new Vector3(
                        sinT * Mathf.Cos(phi) * radius,
                        sinT * Mathf.Sin(phi) * radius,
                        cosT * radius);
                }
            }

            var mesh = new Mesh();
            mesh.name = "HelmVR_ComfortVignetteMesh";
            mesh.vertices = verts;
            mesh.subMeshCount = bands;

            for (int b = 0; b < bands; b++)
            {
                var tris = new int[slices * 6];
                int t = 0;
                for (int s = 0; s < slices; s++)
                {
                    int i00 = b * vertCols + s;
                    int i10 = (b + 1) * vertCols + s;
                    int i01 = i00 + 1;
                    int i11 = i10 + 1;

                    // Inward-facing (clockwise from origin looking +Z in Unity's left-handed space).
                    tris[t++] = i00;
                    tris[t++] = i01;
                    tris[t++] = i10;

                    tris[t++] = i01;
                    tris[t++] = i11;
                    tris[t++] = i10;
                }
                mesh.SetTriangles(tris, b);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
