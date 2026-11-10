using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.WorldShift
{
    [DisallowMultipleComponent]
    public sealed class WorldShiftTransitionController : MonoBehaviour
    {
        private const float MaxUpdateDeltaTime = 1f / 30f;

        [Header("References")]
        [SerializeField] private WorldShiftTransitionProfile profile;
        [SerializeField] private Transform scanOrigin;
        [SerializeField] private Transform scanFrontRoot;
        [SerializeField] private Transform participantRoot;
        [SerializeField] private WorldShiftParticipant[] participants;

        [Header("State")]
        [SerializeField] private WorldShiftLayer initialLayer;
        [SerializeField] private bool playOnStart;
        [SerializeField] private WorldShiftLayer playOnStartTarget = WorldShiftLayer.Underworld;
        [SerializeField, Min(0f)] private float playOnStartDelay = 0.25f;
        [SerializeField, Min(0)] private int playOnStartWarmupFrames = 3;
        [SerializeField] private WorldShiftTransitionMode stencilMode = WorldShiftTransitionMode.RadialScan;

        private float elapsed;
        private float scanFromRadius;
        private float scanToRadius;
        private float currentScanRadius;
        private Vector3 activeScanOrigin;
        private WorldShiftLayer targetLayer;
        private bool ownsScanFrontRoot;
        private bool playSessionStarted;
        private Coroutine playOnStartRoutine;

        public event Action<WorldShiftLayer> TransitionStarted;
        public event Action<WorldShiftLayer> TransitionCompleted;

        public WorldShiftTransitionProfile Profile
        {
            get => profile;
            set
            {
                profile = value;
                UpdateStencilRuntimeRegistration();
            }
        }

        public Transform ScanOrigin
        {
            get => scanOrigin;
            set => scanOrigin = value;
        }

        public Transform ScanFrontRoot
        {
            get => scanFrontRoot;
            set => scanFrontRoot = value;
        }

        public Transform ParticipantRoot
        {
            get => participantRoot;
            set => participantRoot = value;
        }

        public WorldShiftLayer InitialLayer
        {
            get => initialLayer;
            set => initialLayer = value;
        }

        public bool PlayOnStart
        {
            get => playOnStart;
            set => playOnStart = value;
        }

        public WorldShiftLayer PlayOnStartTarget
        {
            get => playOnStartTarget;
            set => playOnStartTarget = value;
        }

        public float PlayOnStartDelay
        {
            get => playOnStartDelay;
            set => playOnStartDelay = Mathf.Max(0f, value);
        }

        public int PlayOnStartWarmupFrames
        {
            get => playOnStartWarmupFrames;
            set => playOnStartWarmupFrames = Mathf.Max(0, value);
        }

        public WorldShiftTransitionMode StencilMode
        {
            get => stencilMode;
            set => stencilMode = value;
        }

        public bool IsPlaying { get; private set; }

        public WorldShiftLayer CurrentLayer { get; private set; }

        private void OnEnable()
        {
            ResetToInitialLayer();
            UpdateStencilRuntimeRegistration();
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            BeginPlaySession(false);
        }

        private void OnDisable()
        {
            WorldShiftStencilRuntimeState.Unregister(this);
            if (playOnStartRoutine != null)
            {
                StopCoroutine(playOnStartRoutine);
                playOnStartRoutine = null;
            }

            IsPlaying = false;
            playSessionStarted = false;
            SetScanGlobalsInactive();
            SetScanFrontVisible(false);
        }

        private void OnDestroy()
        {
            WorldShiftStencilRuntimeState.Unregister(this);
            SetScanGlobalsInactive();
            if (ownsScanFrontRoot && scanFrontRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(scanFrontRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(scanFrontRoot.gameObject);
                }
            }
        }

        private void Update()
        {
            Tick(ClampUpdateDeltaTime(Time.deltaTime));
        }

        public void PlayTo(WorldShiftLayer target, Transform originOverride = null)
        {
            if (!Application.isPlaying && !Application.isEditor)
            {
                return;
            }

            if (!IsPlaying && target == CurrentLayer)
            {
                SnapTo(CurrentLayer);
                return;
            }

            bool wasPlaying = IsPlaying;
            targetLayer = target;
            elapsed = 0f;
            IsPlaying = true;
            activeScanOrigin = originOverride != null ? originOverride.position : GetScanOriginPosition();

            scanFromRadius = wasPlaying
                ? currentScanRadius
                : targetLayer == WorldShiftLayer.Underworld
                    ? ScanStartRadius
                    : ScanEndRadius;
            scanToRadius = targetLayer == WorldShiftLayer.Underworld ? ScanEndRadius : ScanStartRadius;

            SetParticipantsVisible(true);
            EnsureScanFrontRoot();
            SetScanFrontVisible(true);
            ApplyScanState(0f);
            RegisterActiveStencilState();
            TransitionStarted?.Invoke(targetLayer);
        }

        public void Toggle(Transform originOverride = null)
        {
            WorldShiftLayer target = IsPlaying ? Opposite(targetLayer) : Opposite(CurrentLayer);
            PlayTo(target, originOverride);
        }

        public void SnapTo(WorldShiftLayer layer)
        {
            IsPlaying = false;
            CurrentLayer = layer;
            targetLayer = layer;
            activeScanOrigin = GetScanOriginPosition();
            currentScanRadius = layer == WorldShiftLayer.Underworld ? ScanEndRadius : ScanStartRadius;
            ApplyStableLayer();
            SetScanGlobalsInactive();
            EnsureScanFrontRoot();
            PositionScanFront(activeScanOrigin, currentScanRadius);
            SetScanFrontVisible(false);
        }

        public void SetParticipants(IEnumerable<WorldShiftParticipant> newParticipants)
        {
            if (newParticipants == null)
            {
                participants = Array.Empty<WorldShiftParticipant>();
                return;
            }

            var list = new List<WorldShiftParticipant>();
            foreach (var participant in newParticipants)
            {
                if (participant != null && !list.Contains(participant))
                {
                    list.Add(participant);
                }
            }

            participants = list.ToArray();
        }

        public void RebuildParticipants()
        {
            Transform searchRoot = participantRoot != null ? participantRoot : transform;
            participants = searchRoot.GetComponentsInChildren<WorldShiftParticipant>(true);
        }

        public void Tick(float deltaTime)
        {
            if (!IsPlaying)
            {
                return;
            }

            SetParticipantsVisible(true);
            RegisterActiveStencilState();
            elapsed += Mathf.Max(0f, deltaTime);
            float progress = Mathf.Clamp01(Duration > 0f ? elapsed / Duration : 1f);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            ApplyScanState(eased);

            if (progress >= 1f)
            {
                CurrentLayer = targetLayer;
                IsPlaying = false;
                WorldShiftStencilRuntimeState.ClearActiveState(this);
                SetScanGlobalsInactive();
                ApplyStableLayer();
                SetScanFrontVisible(false);
                TransitionCompleted?.Invoke(CurrentLayer);
            }
        }

        private void ApplyStableLayer()
        {
            if (participants == null || participants.Length == 0)
            {
                return;
            }

            for (int i = 0; i < participants.Length; i++)
            {
                var participant = participants[i];
                if (participant != null)
                {
                    participant.SetRenderersVisible(participant.Layer == CurrentLayer);
                }
            }
        }

        private void SetParticipantsVisible(bool visible)
        {
            if (participants == null || participants.Length == 0)
            {
                RebuildParticipants();
            }

            for (int i = 0; i < participants.Length; i++)
            {
                if (participants[i] != null)
                {
                    participants[i].SetRenderersVisible(visible);
                }
            }
        }

        private void ApplyScanState(float progress)
        {
            float radius = Mathf.Lerp(scanFromRadius, scanToRadius, progress);
            currentScanRadius = radius;

            WorldShiftShaderGlobals.SetActive(
                activeScanOrigin,
                radius,
                EdgeWidth,
                targetLayer == WorldShiftLayer.Underworld ? 1f : -1f,
                NoiseScale,
                NoiseStrength,
                EdgeColor);
            PositionScanFront(activeScanOrigin, radius);
        }

        private void SetScanGlobalsInactive()
        {
            WorldShiftShaderGlobals.SetInactive(
                activeScanOrigin,
                currentScanRadius,
                EdgeWidth,
                targetLayer == WorldShiftLayer.Underworld ? 1f : -1f,
                NoiseScale,
                NoiseStrength,
                EdgeColor);
        }

        private Vector3 GetScanOriginPosition()
        {
            return scanOrigin != null ? scanOrigin.position : transform.position;
        }

        private void PositionScanFront(Vector3 origin, float radius)
        {
            if (scanFrontRoot == null)
            {
                return;
            }

            scanFrontRoot.position = origin + Vector3.up * ScanFrontYOffset;
            float diameter = Mathf.Max(radius * 2f, 0.001f);
            scanFrontRoot.localScale = new Vector3(diameter, diameter, diameter);
        }

        private void SetScanFrontVisible(bool visible)
        {
            if (scanFrontRoot != null)
            {
                scanFrontRoot.gameObject.SetActive(visible);
            }
        }

        private void EnsureScanFrontRoot()
        {
            if (scanFrontRoot != null || profile == null || profile.ScanFrontPrefab == null)
            {
                return;
            }

            GameObject instance = Instantiate(profile.ScanFrontPrefab, transform);
            instance.name = profile.ScanFrontPrefab.name;
            scanFrontRoot = instance.transform;
            ownsScanFrontRoot = true;
        }

        private static WorldShiftLayer Opposite(WorldShiftLayer layer)
        {
            return layer == WorldShiftLayer.Overworld ? WorldShiftLayer.Underworld : WorldShiftLayer.Overworld;
        }

        private static float ClampUpdateDeltaTime(float deltaTime)
        {
            return Mathf.Min(Mathf.Max(0f, deltaTime), MaxUpdateDeltaTime);
        }

        private void ResetToInitialLayer()
        {
            IsPlaying = false;
            CurrentLayer = initialLayer;
            targetLayer = initialLayer;
            activeScanOrigin = GetScanOriginPosition();
            currentScanRadius = initialLayer == WorldShiftLayer.Underworld ? ScanEndRadius : ScanStartRadius;
            if (participants == null || participants.Length == 0)
            {
                RebuildParticipants();
            }

            ApplyStableLayer();
            WorldShiftStencilRuntimeState.ClearActiveState(this);
            SetScanGlobalsInactive();
            EnsureScanFrontRoot();
            PositionScanFront(activeScanOrigin, currentScanRadius);
            SetScanFrontVisible(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ResetAllControllersForPlayMode()
        {
            var controllers = FindObjectsOfType<WorldShiftTransitionController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] != null)
                {
                    controllers[i].BeginPlaySession(true);
                }
            }
        }

        private void BeginPlaySession(bool force)
        {
            if (playSessionStarted && !force)
            {
                return;
            }

            if (playOnStartRoutine != null)
            {
                StopCoroutine(playOnStartRoutine);
                playOnStartRoutine = null;
            }

            playSessionStarted = true;
            ResetToInitialLayer();
            UpdateStencilRuntimeRegistration();

            if (!Application.isPlaying || !playOnStart)
            {
                return;
            }

            if (playOnStartDelay > 0f || playOnStartWarmupFrames > 0)
            {
                playOnStartRoutine = StartCoroutine(PlayOnStartAfterDelay());
            }
            else
            {
                PlayTo(playOnStartTarget);
            }
        }

        private IEnumerator PlayOnStartAfterDelay()
        {
            yield return null;

            int warmupFrames = Mathf.Max(0, playOnStartWarmupFrames);
            for (int i = 0; i < warmupFrames; i++)
            {
                yield return new WaitForEndOfFrame();
            }

            float startTime = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startTime < playOnStartDelay)
            {
                yield return null;
            }

            playOnStartRoutine = null;
            if (isActiveAndEnabled && playOnStart)
            {
                PlayTo(playOnStartTarget);
            }
        }

        private float Duration => profile != null ? profile.Duration : 1.35f;
        private float ScanStartRadius => profile != null ? profile.ScanStartRadius : 0f;
        private float ScanEndRadius => profile != null ? profile.ScanEndRadius : 10.8f;
        private float EdgeWidth => profile != null ? profile.EdgeWidth : 0.34f;
        private float NoiseScale => profile != null ? profile.NoiseScale : 1.2f;
        private float NoiseStrength => profile != null ? profile.NoiseStrength : 0.04f;
        private Color EdgeColor => profile != null ? profile.EdgeColor : new Color(0.42f, 0.96f, 1f, 1f);
        private float ScanFrontYOffset => profile != null ? profile.ScanFrontYOffset : 0f;

        private void UpdateStencilRuntimeRegistration()
        {
            if (profile != null &&
                profile.RenderBackend == WorldShiftRenderBackend.StencilRendererFeature &&
                isActiveAndEnabled)
            {
                WorldShiftStencilRuntimeState.Register(this);
                if (IsPlaying)
                {
                    RegisterActiveStencilState();
                }
            }
            else
            {
                WorldShiftStencilRuntimeState.Unregister(this);
            }
        }

        private void RegisterActiveStencilState()
        {
            if (profile == null ||
                profile.RenderBackend != WorldShiftRenderBackend.StencilRendererFeature ||
                !isActiveAndEnabled)
            {
                return;
            }

            WorldShiftStencilRuntimeState.RegisterScan(this, stencilMode, CurrentLayer, targetLayer);
        }
    }
}
