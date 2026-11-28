using System.Collections.Generic;
using System.Reflection;
using cakeslice;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace HelmVR
{
    [DefaultExecutionOrder(50)]
    internal sealed class VrRigController : MonoBehaviour
    {
        private const float SmoothTurnDeadzone = 0.2f;
        private const float SnapResetFactor = 0.55f;
        private const float SceneLookupRetrySeconds = 0.25f;

        internal static VrRigController Instance { get; private set; }

        private static Camera _frameMainCamera;
        private static int _frameMainCameraFrame = -1;

        // Camera.main searches by tag on every call in Unity 2019.1, so look it up once per frame.
        internal static Camera MainCamera
        {
            get
            {
                if (_frameMainCameraFrame != Time.frameCount || _frameMainCamera == null)
                {
                    _frameMainCamera = Camera.main;
                    _frameMainCameraFrame = Time.frameCount;
                }
                return _frameMainCamera;
            }
        }

        private readonly XrInputReader _xr = new XrInputReader();
        private readonly HandVisuals _hands = new HandVisuals();
        private readonly ComfortVignette _vignette = new ComfortVignette();

        internal VrHandInteractor HandInteractor { get; private set; }

        private bool _vrActive;
        private bool _dumpedGameplayDiagnostics;
        private bool _loggedStandby;
        private bool _snapLatched;
        private bool _isSmoothTurningThisFrame;

        private HeadBob _headBob;
        private PlayerCrouching _crouching;
        private Camera _uiCamera;
        private OutlineEffect _mainOutline;
        private OutlineEffect _uiOutline;
        private StartMenu _startMenu;
        private SleepUI _sleepUi;
        private Transform _needsUiAnchor;
        private Transform _missionUiAnchor;
        private bool _savedAnchorBaselines;
        private Vector3 _needsAnchorBasePos;
        private Quaternion _needsAnchorBaseRot = Quaternion.identity;
        private Vector3 _missionAnchorBasePos;
        private Quaternion _missionAnchorBaseRot = Quaternion.identity;
        private float _menuYawOffset;
        private bool _hadSavedPauseUiBase;
        private Vector3 _pauseUiBaseLocalPos;
        private Quaternion _pauseUiBaseLocalRot = Quaternion.identity;
        private GameObject _blackoutDome;
        private Material _blackoutMat;
        private Transform _untrackedEye;
        private Camera _fadeCamera;
        private OVRScreenFade _screenFade;
        private float _nextSceneLookupTime;

        // UUVR RelativeMatrix child camera hierarchy (MainCamera/UICamera -> VrCameraOffset -> VrChildCamera)
        private Camera _uuvrCamOwner;
        private Transform _uuvrOffsetTransform;
        private Transform _uuvrChildCamTransform;
        private Camera _uiUuvrCamOwner;
        private Transform _uiUuvrChildCamTransform;

        // Sleep 3D world-culling state (restored immediately on wake, pause menu, scene load, or disable)
        private Camera _sleepCulledCamera;
        private int _savedSleepCullMask;
        private CameraClearFlags _savedSleepClearFlags;
        private Color _savedSleepBgColor;
        private readonly List<Behaviour> _disabledSleepEffects = new List<Behaviour>();

        private const float AutoRecenterDelaySeconds = 1f;
        private float _autoRecenterTime = -1f;
        private bool _wasPlaying;
        private int _recenterCaptureFrame = -1;
        private bool _recenterIsManual;

        private bool _appliedUiCameraFix;
        private bool _hadSavedHeadBobState;
        private bool _savedHeadBobEnabled;

        internal bool IsVrActive => _vrActive;
        internal HandState LeftHand => _xr.Left;
        internal HandState RightHand => _xr.Right;
        internal Vector3 EyeWorldPosition => _xr.EyeWorldPosition;
        internal Quaternion EyeWorldRotation => _xr.EyeWorldRotation;
        internal Transform EyeTransform => _xr.EyeTransform;

        internal void RefreshHandPosesNow()
        {
            _xr.RefreshWorldPoses();
        }

        private static readonly MethodInfo StartMenuGameToSettings = AccessTools.Method(typeof(StartMenu), "GameToSettings");
        private static readonly MethodInfo StartMenuSettingsToGame = AccessTools.Method(typeof(StartMenu), "SettingsToGame");
        private static readonly FieldInfo StartMenuSettingsUi = AccessTools.Field(typeof(StartMenu), "settingsUI");
        private static readonly FieldInfo StartMenuRecoveryUi = AccessTools.Field(typeof(StartMenu), "recoveryUI");
        private static readonly FieldInfo StartMenuConfirmQuitUi = AccessTools.Field(typeof(StartMenu), "confirmQuitUI");
        private static readonly FieldInfo ScreenFadeRendererField = AccessTools.Field(typeof(OVRScreenFade), "fadeRenderer");
        private static readonly FieldInfo ScreenFadeMaterialField = AccessTools.Field(typeof(OVRScreenFade), "fadeMaterial");
        private static readonly FieldInfo SleepUiParticlesField = AccessTools.Field(typeof(SleepUI), "particles");
        private static readonly FieldInfo SleepUiRecoveryParticlesField = AccessTools.Field(typeof(SleepUI), "recoveryParticles");

        private void Awake()
        {
            Instance = this;
            HandInteractor = new VrHandInteractor(this, _xr.Left, _xr.Right);
            // Also covers a controller recreated after its scene already loaded.
            _autoRecenterTime = Time.unscaledTime + AutoRecenterDelaySeconds;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.onBeforeRender -= OnBeforeRender;
            RestoreSleepCulledCamera();
            ResetHeadLeanTransform();
            HandInteractor?.RestoreDesktopPointer();
            _hands.Destroy();
            _vignette.Destroy();
            if (_blackoutDome != null)
            {
                Destroy(_blackoutDome);
                _blackoutDome = null;
            }
            if (Instance == this) Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                RestoreSleepCulledCamera();
                _headBob = null;
                _crouching = null;
                _uiCamera = null;
                _mainOutline = null;
                _uiOutline = null;
                _startMenu = null;
                _sleepUi = null;
                _uuvrCamOwner = null;
                _uuvrOffsetTransform = null;
                _uuvrChildCamTransform = null;
                _uiUuvrCamOwner = null;
                _uiUuvrChildCamTransform = null;
                _xr.TrackingRootOverride = null;
                _needsUiAnchor = null;
                _missionUiAnchor = null;
                _savedAnchorBaselines = false;
                _hadSavedPauseUiBase = false;
                _menuYawOffset = 0f;
                _appliedUiCameraFix = false;
                _hadSavedHeadBobState = false;
                _fadeCamera = null;
                _screenFade = null;
                _nextSceneLookupTime = 0f;
                _autoRecenterTime = Time.unscaledTime + AutoRecenterDelaySeconds;
                _vignette.HideImmediate();
                MenuPointerPatches.ResetVisualState();
                HandInteractor?.ResetSceneReferences();
            }
        }

        private void Update()
        {
            bool xrNow = XRSettings.enabled && XRSettings.isDeviceActive;
            if (!xrNow)
            {
                if (_vrActive)
                {
                    _vrActive = false;
                    RestoreSleepCulledCamera();
                    ResetHeadLeanTransform();
                    HandInteractor?.RestoreDesktopPointer();
                    _hands.Destroy();
                    _vignette.Destroy();
                    MenuPointerPatches.ResetVisualState();
                }
                else if (!_loggedStandby && Time.unscaledTime > 5f)
                {
                    _loggedStandby = true;
                    Plugin.Log.LogInfo("No active XR headset detected; HelmVR standing by.");
                }
                return;
            }

            if (!_vrActive)
            {
                _vrActive = true;
                Plugin.Log.LogInfo("Active XR headset detected; enabling HelmVR " + Plugin.Version + ".");
            }

            ResolveSceneObjects();
            ApplyStaticRigSettings();

            _xr.Update(Plugin.LogButtonEventsConfig.Value, Plugin.Log);
            VrInputPatches.UpdateToggleStates();
            MenuPointerPatches.UpdateHandSelection(_xr.Left, _xr.Right);

            if (!_dumpedGameplayDiagnostics && _uiCamera != null && MainCamera != null)
            {
                _dumpedGameplayDiagnostics = true;
                DumpFullDiagnostics();
            }

            bool bothSticksClicked = _xr.Left.StickClickPressed && _xr.Right.StickClickPressed
                && (_xr.Left.StickClickDown || _xr.Right.StickClickDown);
            if (bothSticksClicked || Plugin.RecenterKey.Check(1))
            {
                BeginRecenter(true);
            }
            UpdateAutomaticRecenter();
            FinishPendingRecenter();

            if (Plugin.DiagnosticsKey.Check(1))
            {
                DumpFullDiagnostics();
            }

            if (IsPauseButtonDown())
            {
                TryTogglePauseMenu();
            }

            ApplyTurning();
        }

        private void LateUpdate()
        {
            if (!_vrActive) return;

            ApplyHeightCalibration();
            ResolveUuvrCameraChildren(MainCamera);

            // Refresh world hand poses after PlayerControllerMirror moves the player with the boat,
            // then run HandInteractor so raycasts and visual lasers use the exact same boat-synced position.
            _xr.RefreshWorldPoses();
            ApplyHeadLean();
            SyncMouthColliderToEye();
            SyncNeedsUiDistance();

            HandInteractor.Update();
            HandInteractor.LateUpdate();
            SyncSleepBlackoutDome();
            UpdateComfortVignette();
            // Holding B shows the aim laser, or Y when B is the pause button.
            ControllerButton laserButton = Plugin.PauseButtonConfig.Value == ControllerButton.B ? ControllerButton.Y : ControllerButton.B;
            bool holdAimLaser = ControllerButtons.Check(laserButton, _xr.Left, _xr.Right, 0);
            _hands.Update(HandInteractor.Left, HandInteractor.Right, Plugin.ShowHandVisualsConfig.Value, GameState.inCursorMenu, holdAimLaser);

            if (Plugin.HideFlatCrosshairConfig.Value && Refs.mouseCrosshair != null && Refs.mouseCrosshair.activeSelf)
            {
                Refs.mouseCrosshair.SetActive(false);
            }
        }

        private void OnBeforeRender()
        {
            if (!_vrActive) return;
            ResolveUuvrCameraChildren(MainCamera);
            if (_uuvrChildCamTransform == null && _uiUuvrChildCamTransform == null) return;

            _xr.UpdateHeadPose();
            bool allowLean = Plugin.EnableHeadLeaningConfig.Value && !BoatCamera.on;
            Vector3 offset = _xr.ComputeClampedHeadOffset(allowLean, Plugin.MaxLeanDistanceConfig.Value);
            if (_uuvrChildCamTransform != null) _uuvrChildCamTransform.localPosition = offset;
            if (_uiUuvrChildCamTransform != null) _uiUuvrChildCamTransform.localPosition = offset;
        }

        private void ResolveUuvrCameraChildren(Camera mainCam)
        {
            if (mainCam == null)
            {
                _uuvrCamOwner = null;
                _uuvrOffsetTransform = null;
                _uuvrChildCamTransform = null;
                _xr.TrackingRootOverride = null;
            }
            else if (mainCam != _uuvrCamOwner || _uuvrChildCamTransform == null || _uuvrOffsetTransform == null)
            {
                _uuvrCamOwner = mainCam;
                _uuvrOffsetTransform = mainCam.transform.Find("VrCameraOffset");
                _uuvrChildCamTransform = _uuvrOffsetTransform != null
                    ? _uuvrOffsetTransform.Find("VrChildCamera")
                    : mainCam.transform.Find("VrChildCamera");
                _xr.TrackingRootOverride = _uuvrOffsetTransform;
            }

            if (_uiCamera == null)
            {
                _uiUuvrCamOwner = null;
                _uiUuvrChildCamTransform = null;
            }
            else if (_uiCamera != _uiUuvrCamOwner || _uiUuvrChildCamTransform == null)
            {
                _uiUuvrCamOwner = _uiCamera;
                Transform uiOffset = _uiCamera.transform.Find("VrCameraOffset");
                _uiUuvrChildCamTransform = uiOffset != null
                    ? uiOffset.Find("VrChildCamera")
                    : _uiCamera.transform.Find("VrChildCamera");
            }
        }

        private void ApplyHeadLean()
        {
            Vector3 offset = _xr.AppliedHeadOffset;
            if (_uuvrChildCamTransform != null) _uuvrChildCamTransform.localPosition = offset;
            if (_uiUuvrChildCamTransform != null) _uiUuvrChildCamTransform.localPosition = offset;
        }

        private void ResetHeadLeanTransform()
        {
            if (_uuvrChildCamTransform != null)
            {
                _uuvrChildCamTransform.localPosition = Vector3.zero;
            }
            if (_uiUuvrChildCamTransform != null)
            {
                _uiUuvrChildCamTransform.localPosition = Vector3.zero;
            }
        }

        // Wherever you happened to face while the game loaded becomes "forward" unless the view is recentered,
        // so recenter automatically once each scene has loaded and again once a save has finished loading.
        private void UpdateAutomaticRecenter()
        {
            bool playing = GameState.playing;
            if (playing && !_wasPlaying)
            {
                _autoRecenterTime = Time.unscaledTime + AutoRecenterDelaySeconds;
            }
            _wasPlaying = playing;

            if (_autoRecenterTime >= 0f && Time.unscaledTime >= _autoRecenterTime && !GameState.currentlyLoading)
            {
                _autoRecenterTime = -1f;
                BeginRecenter(false);
            }
        }

        private void BeginRecenter(bool manual)
        {
            _xr.ResetTracking();
            // The tracking reset shows up in the head pose a frame or two later; reading it now would keep the old offset.
            _recenterCaptureFrame = Time.frameCount + 2;
            _recenterIsManual = manual;
        }

        private void FinishPendingRecenter()
        {
            if (_recenterCaptureFrame < 0 || Time.frameCount < _recenterCaptureFrame) return;
            _recenterCaptureFrame = -1;

            _xr.CaptureHeadOrigin();
            _menuYawOffset = _xr.HeadLocalRotation.eulerAngles.y;
            SnapActivePauseMenuToHeadYaw();
            if (_recenterIsManual)
            {
                _xr.Left.SendHaptic(0.4f, 0.04f);
                _xr.Right.SendHaptic(0.4f, 0.04f);
            }
            Plugin.Log.LogInfo($"[VR] Recentered head and menus ({(_recenterIsManual ? "manual" : "automatic")}, menuYaw={_menuYawOffset:F1} deg).");
        }

        private bool IsPauseButtonDown()
        {
            if (_xr.Left.MenuDown || _xr.Right.MenuDown) return true;
            return ControllerButtons.Check(Plugin.PauseButtonConfig.Value, _xr.Left, _xr.Right, 1);
        }

        private void ResolveSceneObjects()
        {
            // Name and type searches are expensive, so retry missing objects a few times a second rather than every frame.
            if (Time.unscaledTime < _nextSceneLookupTime) return;

            Camera mainCam = MainCamera;
            if (_headBob == null && Refs.ovrCameraRig != null)
            {
                _headBob = Refs.ovrCameraRig.GetComponent<HeadBob>();
                _crouching = Refs.ovrCameraRig.GetComponent<PlayerCrouching>();
            }

            if (_uiCamera == null)
            {
                GameObject uiCamGo = GameObject.Find("UI camera");
                if (uiCamGo != null)
                {
                    _uiCamera = uiCamGo.GetComponent<Camera>();
                    _appliedUiCameraFix = false;
                }
            }

            if (_needsUiAnchor == null && mainCam != null)
            {
                Transform found = mainCam.transform.Find("needs UI anchor (PC)");
                if (found != null)
                {
                    _needsUiAnchor = found;
                    _needsAnchorBasePos = found.localPosition;
                    _needsAnchorBaseRot = found.localRotation;
                }
            }

            if (_missionUiAnchor == null && mainCam != null)
            {
                Transform found = mainCam.transform.Find("mission UI anchor (PC)");
                if (found != null)
                {
                    _missionUiAnchor = found;
                    _missionAnchorBasePos = found.localPosition;
                    _missionAnchorBaseRot = found.localRotation;
                }
            }

            if (!_savedAnchorBaselines && (_needsUiAnchor != null || _missionUiAnchor != null))
            {
                _savedAnchorBaselines = true;
            }

            if (_mainOutline == null && mainCam != null)
            {
                _mainOutline = mainCam.GetComponent<OutlineEffect>();
            }

            if (_uiOutline == null && _uiCamera != null)
            {
                _uiOutline = _uiCamera.GetComponent<OutlineEffect>();
            }

            if (_startMenu == null)
            {
                _startMenu = FindObjectOfType<StartMenu>();
            }

            if (_sleepUi == null)
            {
                _sleepUi = FindObjectOfType<SleepUI>();
            }

            bool allFound = _headBob != null && _uiCamera != null && _needsUiAnchor != null && _missionUiAnchor != null
                && _mainOutline != null && _uiOutline != null && _startMenu != null && _sleepUi != null;
            _nextSceneLookupTime = allFound ? 0f : Time.unscaledTime + SceneLookupRetrySeconds;
        }

        private void ApplyStaticRigSettings()
        {
            Settings.VRLookEnabled = true;

            if (Refs.ovrController != null)
            {
                Refs.ovrController.EnableRotation = false;
            }

            if (_headBob != null)
            {
                if (!_hadSavedHeadBobState)
                {
                    _savedHeadBobEnabled = _headBob.enabled;
                    _hadSavedHeadBobState = true;
                }

                bool desiredHeadBob = !Plugin.DisableHeadBobConfig.Value && _savedHeadBobEnabled;
                if (_headBob.enabled != desiredHeadBob)
                {
                    _headBob.enabled = desiredHeadBob;
                }
            }

            // The UI camera is a child of the eye camera, so its own XR tracking and 50 cm offset would apply head motion twice.
            if (_uiCamera != null && !_appliedUiCameraFix)
            {
                XRDevice.DisableAutoXRCameraTracking(_uiCamera, true);
                _uiCamera.transform.localPosition = Vector3.zero;
                _uiCamera.transform.localRotation = Quaternion.identity;
                _uiCamera.nearClipPlane = 0.05f;
                _appliedUiCameraFix = true;
                Plugin.Log.LogInfo($"UI camera VR fix applied (parent={GetPath(_uiCamera.transform.parent)}).");
            }

            // Sailwind's outline effect is a mono image effect whose highlight doesn't line up between eyes, so keep it off in VR.
            if (_mainOutline != null && _mainOutline.enabled)
            {
                _mainOutline.enabled = false;
            }
            if (_uiOutline != null && _uiOutline.enabled)
            {
                _uiOutline.enabled = false;
            }

            Camera mainCam = MainCamera;
            if (mainCam != null)
            {
                bool wantOcc = !Plugin.DisableOcclusionCullingConfig.Value;
                if (mainCam.useOcclusionCulling != wantOcc)
                {
                    mainCam.useOcclusionCulling = wantOcc;
                }
            }
        }

        // While the game loads, its desktop MouseLook and Unity's own XR camera tracking (before UUVR takes the camera
        // over) can leave a turn or tilt baked into the head, rig, tracking space or eye objects, and nothing undoes it.
        // UUVR adds the headset pose on top of these transforms, so clear the head's pitch and roll (keeping its heading),
        // keep the rig unrotated under it, and keep the tracking space and eye at the rig's origin. The rig stays
        // relative to the head rather than world-level because PlayerClimb (ratlines) and oars read its local rotation.
        private void KeepRigLevelAndClean()
        {
            Transform rig = Refs.ovrCameraRig;
            if (rig.localRotation != Quaternion.identity)
            {
                LogRigCorrection(rig, Quaternion.Angle(rig.localRotation, Quaternion.identity), 0f);
                rig.localRotation = Quaternion.identity;
            }

            Transform head = rig.parent;
            if (head != null)
            {
                Vector3 e = head.localEulerAngles;
                float tilt = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)), Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)));
                if (tilt > 0.05f)
                {
                    LogRigCorrection(head, tilt, 0f);
                    head.localRotation = Quaternion.Euler(0f, e.y, 0f);
                }
            }

            Camera mainCam = MainCamera;
            if (mainCam == null) return;
            Transform eye = mainCam.transform;
            // Only the rig's own eye camera; another camera tagged MainCamera (the chart table's, or a mod's) is left
            // alone, and its parents are never reset.
            if (!eye.IsChildOf(rig)) return;
            if (eye != _untrackedEye)
            {
                // Same call UUVR makes once it adopts the camera; doing it as soon as the camera exists stops Unity
                // from writing the headset pose into the eye transform during loading.
                XRDevice.DisableAutoXRCameraTracking(mainCam, true);
                _untrackedEye = eye;
            }
            for (Transform t = eye; t != null && t != rig; t = t.parent)
            {
                if (t.localRotation != Quaternion.identity || t.localPosition != Vector3.zero)
                {
                    LogRigCorrection(t, Quaternion.Angle(t.localRotation, Quaternion.identity), t.localPosition.magnitude);
                    t.localRotation = Quaternion.identity;
                    t.localPosition = Vector3.zero;
                }
            }
        }

        private int _rigCorrectionLogs;

        // Evidence for where a load-time turn or tilt came from; only noticeable corrections are logged, and only a few.
        private void LogRigCorrection(Transform t, float degrees, float meters)
        {
            if ((degrees < 2f && meters < 0.02f) || _rigCorrectionLogs >= 12) return;
            _rigCorrectionLogs++;
            Plugin.Log.LogInfo($"[VR] Cleared {degrees:F1} deg / {meters:F2} m baked into '{t.name}' (playing={GameState.playing}).");
        }

        private void ApplyHeightCalibration()
        {
            if (Refs.ovrCameraRig == null) return;
            if (GameState.inBed != null || BoatCamera.on) return;

            KeepRigLevelAndClean();

            if (!GameState.playing) return;

            // Under UUVR RelativeMatrix, eyes render at CenterEyeAnchor (OVRCameraRig.localPosition.y),
            // so BaseEyeHeight defaults to Sailwind's ~1.05 m eye level, plus PlayerCrouching delta when crouched.
            float crouchDelta = 0f;
            if (_crouching != null)
            {
                float h = _crouching.GetCurrentHeadHeight();
                if (h > 0.05f && h < 1.1f)
                {
                    crouchDelta = h - 1.1f;
                }
            }
            float targetY = Plugin.BaseEyeHeightConfig.Value + Plugin.HeightOffsetConfig.Value + crouchDelta;

            Vector3 localPos = Refs.ovrCameraRig.localPosition;
            if (Mathf.Abs(localPos.y - targetY) > 0.0005f)
            {
                localPos.y = targetY;
                Refs.ovrCameraRig.localPosition = localPos;
            }
        }

        private void SyncMouthColliderToEye()
        {
            if (Refs.playerMouthCol == null || _xr.EyeTransform == null) return;
            Refs.playerMouthCol.transform.position = _xr.EyeWorldPosition + _xr.EyeWorldRotation * new Vector3(0f, -0.14f, 0.05f);
        }

        private void SyncSleepBlackoutDome()
        {
            // Keep trackingSpace horizon-level in LateUpdate (after boat physics) while in bed/sleeping
            // so wave pitch/roll at 16x speed does not make the sleep bar bounce up and down in VR.
            if (Plugin.ComfortSleepHorizonConfig.Value
                && (GameState.inBed != null || GameState.sleeping)
                && Sleep.instance != null
                && Sleep.instance.trackingSpace != null)
            {
                Vector3 e = Sleep.instance.trackingSpace.eulerAngles;
                Sleep.instance.trackingSpace.eulerAngles = new Vector3(0f, e.y, 0f);
            }

            Camera mainCam = MainCamera;
            if (mainCam != _fadeCamera)
            {
                _fadeCamera = mainCam;
                _screenFade = mainCam != null ? mainCam.GetComponent<OVRScreenFade>() : null;
            }
            OVRScreenFade fade = _screenFade;
            float alpha = fade != null ? fade.currentAlpha : 0f;

            SyncSleepParticles();

            bool shouldCullWorld = Plugin.OptimizeSleepRenderingConfig.Value
                && GameState.sleeping
                && GameState.eyesFullyClosed
                && alpha >= 0.99f
                && !GameState.inCursorMenu
                && mainCam != null;

            if (shouldCullWorld)
            {
                ApplySleepWorldCull(mainCam);
                if (_blackoutDome != null && _blackoutDome.activeSelf)
                {
                    _blackoutDome.SetActive(false);
                }
                var fadeRendererCull = ScreenFadeRendererField?.GetValue(fade) as MeshRenderer;
                if (fadeRendererCull != null && fadeRendererCull.enabled)
                {
                    fadeRendererCull.enabled = false;
                }
                return;
            }

            RestoreSleepCulledCamera();

            if (alpha <= 0.01f)
            {
                if (_blackoutDome != null && _blackoutDome.activeSelf)
                {
                    _blackoutDome.SetActive(false);
                }
                return;
            }

            var fadeRenderer = ScreenFadeRendererField?.GetValue(fade) as MeshRenderer;
            if (fadeRenderer != null && fadeRenderer.enabled)
            {
                fadeRenderer.enabled = false;
            }

            if (_blackoutDome == null)
            {
                _blackoutDome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _blackoutDome.name = "HelmVR_SleepBlackoutDome";
                _blackoutDome.layer = 2;
                Collider col = _blackoutDome.GetComponent<Collider>();
                if (col != null) Destroy(col);

                // Invert sphere winding so inside faces render around the player's eyes in 360 degrees.
                MeshFilter mf = _blackoutDome.GetComponent<MeshFilter>();
                if (mf != null && mf.mesh != null)
                {
                    Mesh mesh = mf.mesh;
                    int[] tris = mesh.triangles;
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        int tmp = tris[i];
                        tris[i] = tris[i + 1];
                        tris[i + 1] = tmp;
                    }
                    mesh.triangles = tris;
                    mesh.RecalculateNormals();
                }

                // Clone OVRScreenFade's own unlit transparent fade material so it is pure uniform black with zero lighting/blanket shading.
                Renderer domeRenderer = _blackoutDome.GetComponent<Renderer>();
                var nativeFadeMat = fade != null ? ScreenFadeMaterialField?.GetValue(fade) as Material : null;
                if (nativeFadeMat != null)
                {
                    _blackoutMat = new Material(nativeFadeMat);
                }
                else
                {
                    _blackoutMat = new Material(domeRenderer.sharedMaterial);
                    _blackoutMat.SetFloat("_Mode", 2f);
                    _blackoutMat.SetFloat("_Glossiness", 0f);
                    _blackoutMat.SetFloat("_SpecularHighlights", 0f);
                    _blackoutMat.SetFloat("_GlossyReflections", 0f);
                    _blackoutMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _blackoutMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _blackoutMat.SetInt("_ZWrite", 0);
                    _blackoutMat.EnableKeyword("_ALPHABLEND_ON");
                }
                _blackoutMat.renderQueue = 5000;
                domeRenderer.sharedMaterial = _blackoutMat;
                domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                domeRenderer.receiveShadows = false;
                _blackoutDome.transform.localScale = Vector3.one * 0.65f;
            }

            if (!_blackoutDome.activeSelf) _blackoutDome.SetActive(true);
            _blackoutDome.transform.position = _xr.EyeWorldPosition;
            _blackoutMat.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
        }

        private void ApplySleepWorldCull(Camera mainCam)
        {
            if (_sleepCulledCamera != null && _sleepCulledCamera != mainCam)
            {
                RestoreSleepCulledCamera();
            }

            if (_sleepCulledCamera == null)
            {
                _sleepCulledCamera = mainCam;
                _savedSleepCullMask = mainCam.cullingMask;
                _savedSleepClearFlags = mainCam.clearFlags;
                _savedSleepBgColor = mainCam.backgroundColor;
                _disabledSleepEffects.Clear();

                Component[] comps = mainCam.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (!(comps[i] is Behaviour b) || !b.enabled) continue;
                    string typeName = b.GetType().Name;
                    if (typeName.IndexOf("AmplifyOcclusion", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.IndexOf("PostProcess", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.IndexOf("Bloom", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.IndexOf("Scattering", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.IndexOf("ToneMapping", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        b.enabled = false;
                        _disabledSleepEffects.Add(b);
                    }
                }
            }

            if (mainCam.cullingMask != 0) mainCam.cullingMask = 0;
            if (mainCam.clearFlags != CameraClearFlags.SolidColor) mainCam.clearFlags = CameraClearFlags.SolidColor;
            if (mainCam.backgroundColor != Color.black) mainCam.backgroundColor = Color.black;
        }

        private void RestoreSleepCulledCamera()
        {
            if (_sleepCulledCamera != null)
            {
                _sleepCulledCamera.cullingMask = _savedSleepCullMask;
                _sleepCulledCamera.clearFlags = _savedSleepClearFlags;
                _sleepCulledCamera.backgroundColor = _savedSleepBgColor;
                _sleepCulledCamera = null;
            }

            for (int i = 0; i < _disabledSleepEffects.Count; i++)
            {
                if (_disabledSleepEffects[i] != null)
                {
                    _disabledSleepEffects[i].enabled = true;
                }
            }
            _disabledSleepEffects.Clear();
        }

        private void SyncSleepParticles()
        {
            if (_sleepUi == null) return;
            if (!GameState.sleeping && !GameState.recovering) return;

            bool show = Plugin.ShowSleepParticlesConfig.Value;
            ApplySleepParticleVisibility(SleepUiParticlesField?.GetValue(_sleepUi) as ParticleSystem, show, GameState.sleeping && !GameState.recovering);
            ApplySleepParticleVisibility(SleepUiRecoveryParticlesField?.GetValue(_sleepUi) as ParticleSystem, show, GameState.recovering);
        }

        private static void ApplySleepParticleVisibility(ParticleSystem ps, bool show, bool shouldEmit)
        {
            if (ps == null) return;
            // Keep sleep particles in Local simulation space so 16x wave heave does not fling them vertically across view.
            ParticleSystem.MainModule main = ps.main;
            if (main.simulationSpace != ParticleSystemSimulationSpace.Local)
            {
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
            }

            Renderer r = ps.GetComponent<Renderer>();
            if (!show)
            {
                if (ps.isPlaying || ps.particleCount > 0)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (r != null && r.enabled) r.enabled = false;
            }
            else
            {
                if (r != null && !r.enabled) r.enabled = true;
                if (shouldEmit && !ps.isPlaying)
                {
                    ParticleSystem.EmissionModule emission = ps.emission;
                    emission.enabled = true;
                    ps.Play(true);
                }
            }
        }

        private void UpdateComfortVignette()
        {
            float stillVal = Plugin.VignetteWhileStillConfig.Value;
            float aboardVal = Plugin.VignetteWhileAboardConfig.Value;
            float movingVal = Plugin.VignetteWhileMovingConfig.Value;

            bool hide = !GameState.playing
                || GameState.inCursorMenu
                || GameState.sleeping
                || BoatCamera.on
                || VrShipyardPatches.IsInShipyard()
                || (stillVal <= 0.001f && aboardVal <= 0.001f && movingVal <= 0.001f);

            if (hide)
            {
                _vignette.Update(_xr.EyeWorldPosition, _xr.EyeWorldRotation, 0f, Time.unscaledDeltaTime);
                return;
            }

            bool isAboard = GameState.currentBoat != null;
            bool isStickMoving = GameState.inBed == null
                && !VrChartingPatches.IsChartingActive()
                && !VrDizzyCompat.IsVrNudgeActive
                && !HandInteractor.IsAnyHandGrippingAnyRotary()
                && _xr.Left.Stick.magnitude >= 0.12f;

            bool isMovingOrTurning = isStickMoving || _isSmoothTurningThisFrame;
            float baseVal = isAboard ? Mathf.Max(stillVal, aboardVal) : stillVal;
            float target = isMovingOrTurning ? Mathf.Max(baseVal, movingVal) : baseVal;
            _vignette.Update(_xr.EyeWorldPosition, _xr.EyeWorldRotation, target, Time.unscaledDeltaTime);
        }

        private void SyncNeedsUiDistance()
        {
            float targetZ = Plugin.NeedsUiDistanceConfig.Value;
            float heightOffset = Plugin.NeedsUiHeightOffsetConfig.Value;
            Quaternion yawOrbit = Quaternion.Euler(0f, _menuYawOffset, 0f);
            Quaternion tilt = Quaternion.Euler(Plugin.NeedsUiTiltAngleConfig.Value, 0f, 0f);

            if (_needsUiAnchor != null)
            {
                Vector3 baseLocal = new Vector3(_needsAnchorBasePos.x, _needsAnchorBasePos.y + heightOffset, targetZ);
                _needsUiAnchor.localPosition = yawOrbit * baseLocal;
                _needsUiAnchor.localRotation = yawOrbit * _needsAnchorBaseRot * tilt;
            }

            if (_missionUiAnchor != null)
            {
                Vector3 baseLocal = new Vector3(_missionAnchorBasePos.x, _missionAnchorBasePos.y + heightOffset, targetZ);
                _missionUiAnchor.localPosition = yawOrbit * baseLocal;
                _missionUiAnchor.localRotation = yawOrbit * _missionAnchorBaseRot * tilt;
            }
        }

        private void SnapActivePauseMenuToHeadYaw()
        {
            if (_startMenu == null) return;
            var settingsUi = StartMenuSettingsUi?.GetValue(_startMenu) as GameObject;
            Transform menuParent = settingsUi != null ? settingsUi.transform.parent : null;
            if (menuParent == null || MainCamera == null) return;

            // Only orbit menu container when it is parented under CenterEyeAnchor / Camera.main.
            if (menuParent.parent != MainCamera.transform) return;

            if (!_hadSavedPauseUiBase)
            {
                _pauseUiBaseLocalPos = menuParent.localPosition;
                _pauseUiBaseLocalRot = menuParent.localRotation;
                _hadSavedPauseUiBase = true;
            }

            Quaternion yawOrbit = Quaternion.Euler(0f, _menuYawOffset, 0f);
            menuParent.localPosition = yawOrbit * _pauseUiBaseLocalPos;
            menuParent.localRotation = yawOrbit * _pauseUiBaseLocalRot;
        }

        private void ApplyTurning()
        {
            _isSmoothTurningThisFrame = false;
            // Allow turning while Inventory/Logbook are open, but block when paused, sleeping, in Shipyard, Charting, or gripping a rotary control.
            if (!GameState.playing
                || Time.timeScale <= 0.01f
                || GameState.sleeping
                || GameState.inBed != null
                || BoatCamera.on
                || VrShipyardPatches.IsInShipyard()
                || VrChartingPatches.IsChartingActive()
                || VrDizzyCompat.IsVrNudgeActive
                || HandInteractor.IsHandGrippingAnyRotary(false))
            {
                _snapLatched = false;
                return;
            }

            if (Refs.observerMirror == null) return;

            float stickX = _xr.Right.Stick.x;
            float deltaYaw = 0f;

            if (Plugin.TurnModeConfig.Value == TurnMode.Snap)
            {
                float threshold = Plugin.TurnDeadzoneConfig.Value;
                float releaseThreshold = threshold * SnapResetFactor;

                if (!_snapLatched && Mathf.Abs(stickX) >= threshold)
                {
                    deltaYaw = Mathf.Sign(stickX) * Plugin.SnapTurnAngleConfig.Value;
                    _snapLatched = true;
                }
                else if (_snapLatched && Mathf.Abs(stickX) <= releaseThreshold)
                {
                    _snapLatched = false;
                }
            }
            else
            {
                _snapLatched = false;
                if (Mathf.Abs(stickX) > SmoothTurnDeadzone)
                {
                    float scaled = (Mathf.Abs(stickX) - SmoothTurnDeadzone) / (1f - SmoothTurnDeadzone);
                    deltaYaw = Mathf.Sign(stickX) * scaled * Plugin.SmoothTurnSpeedConfig.Value * Time.deltaTime;
                    _isSmoothTurningThisFrame = true;
                }
            }

            if (Mathf.Abs(deltaYaw) > 0.0001f)
            {
                Refs.observerMirror.transform.Rotate(Vector3.up, deltaYaw, Space.Self);
            }
        }

        private void TryTogglePauseMenu()
        {
            if (!GameState.playing || GameState.currentlyLoading || GameState.currentShipyard != null || VrChartingPatches.IsChartingActive())
            {
                return;
            }

            if (_startMenu == null)
            {
                Plugin.Log.LogWarning("VR pause button pressed, but StartMenu was not found.");
                return;
            }

            var settingsUi = StartMenuSettingsUi?.GetValue(_startMenu) as GameObject;
            var recoveryUi = StartMenuRecoveryUi?.GetValue(_startMenu) as GameObject;
            var confirmQuitUi = StartMenuConfirmQuitUi?.GetValue(_startMenu) as GameObject;

            bool anyPauseMenuActive =
                (settingsUi != null && settingsUi.activeInHierarchy) ||
                (recoveryUi != null && recoveryUi.activeInHierarchy) ||
                (confirmQuitUi != null && confirmQuitUi.activeInHierarchy);

            if (anyPauseMenuActive)
            {
                StartMenuSettingsToGame?.Invoke(_startMenu, null);
                Plugin.Log.LogInfo("[VR] Pause menu closed.");
            }
            else if (EconomyUI.instance != null && EconomyUI.instance.uiActive)
            {
                EconomyUI.instance.CloseUI();
            }
            else if (CurrencyExchangeUI.instance != null && CurrencyExchangeUI.instance.uiActive)
            {
                CurrencyExchangeUI.instance.CloseUI();
            }
            else if (GameState.inPortMissionList && MissionListUI.instance != null)
            {
                MissionListUI.instance.DisablePortMissionUI();
            }
            else
            {
                _menuYawOffset = _xr.HeadLocalRotation.eulerAngles.y;
                StartMenuGameToSettings?.Invoke(_startMenu, null);
                SnapActivePauseMenuToHeadYaw();
                Plugin.Log.LogInfo("[VR] Pause menu opened.");
            }
        }

        private void DumpFullDiagnostics()
        {
            _xr.DumpAll(Plugin.Log);

            Camera mainCam = Camera.main;
            Plugin.Log.LogInfo($"Camera.main: {(mainCam != null ? GetPath(mainCam.transform) : "null")}");
            if (mainCam != null)
            {
                Plugin.Log.LogInfo($"  mainCam localPos={mainCam.transform.localPosition.ToString("F2")}, depth={mainCam.depth}, targetEye={mainCam.stereoTargetEye}, stereoEnabled={mainCam.stereoEnabled}");
                Component[] comps = mainCam.GetComponents<Component>();
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null) continue;
                    if (names.Length > 0) names.Append(", ");
                    names.Append(comps[i].GetType().Name);
                    if (comps[i] is Behaviour b) names.Append(b.enabled ? "(on)" : "(off)");
                }
                Plugin.Log.LogInfo($"  mainCam components: {names}");
            }

            Plugin.Log.LogInfo($"UI camera: {(_uiCamera != null ? GetPath(_uiCamera.transform) : "null")}");
            if (_uiCamera != null)
            {
                Plugin.Log.LogInfo($"  uiCam localPos={_uiCamera.transform.localPosition.ToString("F2")}, depth={_uiCamera.depth}, targetEye={_uiCamera.stereoTargetEye}, stereoEnabled={_uiCamera.stereoEnabled}, vrFix={_appliedUiCameraFix}");
            }

            if (Refs.ovrCameraRig != null && Refs.observerMirror != null)
            {
                float eyeAboveFeet = (_xr.EyeWorldPosition.y - Refs.observerMirror.transform.position.y) + 0.55f;
                Plugin.Log.LogInfo($"Height: ovrCameraRig.localPos={Refs.ovrCameraRig.localPosition.ToString("F2")}, eyeAboveDeck={eyeAboveFeet:F2}m, base={Plugin.BaseEyeHeightConfig.Value:F2}m, offset={Plugin.HeightOffsetConfig.Value:F2}m");
            }
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "null";
            string path = t.name;
            Transform cur = t.parent;
            while (cur != null)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
            }
            return path;
        }
    }
}
