using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// MediaPipePoseDriver.cs  —  AR Overlay Edition
//
// Projects MediaPipe landmarks directly into camera world-space so the
// character appears overlaid on the tracked person in the webcam feed.
//
// HOW THE OVERLAY WORKS
// ──────────────────────
// Previously ToWorld() used a fixed poseScale to map landmarks to arbitrary
// world units — the character had no relationship to camera or screen.
//
// Now:
//   1. Each landmark is unprojected through the camera using
//      Camera.ScreenToWorldPoint(), giving it a real world position that
//      lines up exactly with what the camera renders.
//   2. The character's transform.localScale is computed each frame so the
//      model's shoulder span matches the tracked person's shoulder span on
//      screen — the skeleton automatically grows/shrinks with distance.
//
// SETUP
// ──────
// 1. Replace PoseLandmarkerRunner.cs with the modified version
// 2. Add this script to your character GameObject (same one as Animator)
// 3. Drag the "Solution" GameObject into the Runner field  
// 4. Assign the scene camera to the Cam field (or leave blank for Camera.main)
// 5. Animator Controller must exist with IK Pass enabled on Base Layer
// 6. Uncheck Apply Root Motion on the Animator
// 7. Adjust Character Depth until the character sits inside the person
// ─────────────────────────────────────────────────────────────────────────────

namespace Mediapipe.Unity.Sample.PoseLandmarkDetection
{
    using Mediapipe.Tasks.Vision.PoseLandmarker;
    using Mediapipe.Tasks.Components.Containers;
    using UnityEngine;

    [RequireComponent(typeof(Animator))]
    public class MediaPipePoseDriver : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Connection")]
        [Tooltip("Drag the Solution GameObject here. Auto-found if left blank.")]
        public PoseLandmarkerRunner runner;

        [Header("Camera & Depth")]
        [Tooltip("The camera rendering the scene. Defaults to Camera.main.")]
        public Camera cam;

        [Tooltip("How far (metres) in front of the camera the character sits. " +
                 "Increase if the character is clipping into the background, " +
                 "decrease if it disappears behind the webcam plane.")]
        public float characterDepth = 2f;

        [Header("Smoothing")]
        [Range(0f, 0.99f)]
        [Tooltip("0 = raw MediaPipe output, 0.85 = smooth but slightly lagged.")]
        public float smoothing = 0.85f;

        [Header("Fine Tuning")]
        [Tooltip("World-space offset applied after projection — useful for " +
                 "nudging the character if it's slightly off-centre.")]
        public Vector3 worldOffset = Vector3.zero;

        [Tooltip("Extra global scale multiplier on top of the auto-computed " +
                 "body scale. 1 = match the person exactly.")]
        public float scaleMultiplier = 1f;

        [Header("IK Weights")]
        [Range(0f, 1f)] public float handIKWeight = 1.0f;
        [Range(0f, 1f)] public float footIKWeight = 1.0f;
        [Range(0f, 1f)] public float hintIKWeight = 0.6f;

        [Header("Confidence Threshold")]
        [Range(0f, 1f)]
        public float visibilityThreshold = 0.5f;

        [Header("Debug")]
        public bool drawDebugGizmos = true;

        // ── Landmark Indices ──────────────────────────────────────────────────

        private const int IDX_NOSE = 0;
        private const int IDX_L_SHOULDER = 11;
        private const int IDX_R_SHOULDER = 12;
        private const int IDX_L_ELBOW = 13;
        private const int IDX_R_ELBOW = 14;
        private const int IDX_L_WRIST = 15;
        private const int IDX_R_WRIST = 16;
        private const int IDX_L_HIP = 23;
        private const int IDX_R_HIP = 24;
        private const int IDX_L_KNEE = 25;
        private const int IDX_R_KNEE = 26;
        private const int IDX_L_ANKLE = 27;
        private const int IDX_R_ANKLE = 28;

        // ── Private State ─────────────────────────────────────────────────────

        private Animator _animator;
        private PoseLandmarkerResult _latestResult;
        private bool _hasNewResult;
        private readonly object _lock = new object();

        private readonly Vector3[] _smoothed = new Vector3[33];
        private readonly bool[] _visible = new bool[33];

        // Bone transforms
        private Transform _hips, _spine, _chest, _upperChest, _neck, _head;
        private Transform _lShoulderBone, _rShoulderBone;

        // T-pose rest rotations
        private Quaternion _hipsRest, _spineRest, _chestRest, _upperChestRest, _neckRest;

        // Reference shoulder span measured from the model in T-pose (world units at scale=1)
        // Used to compute how much to scale the character each frame.
        private float _modelShoulderSpan = 1f;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            _animator = GetComponent<Animator>();

            if (cam == null) cam = Camera.main;

            CacheBones();
        }

        void Start()
        {
            if (runner == null)
                runner = FindObjectOfType<PoseLandmarkerRunner>();

            if (runner == null)
            {
                Debug.LogError("[PoseDriver] PoseLandmarkerRunner not found. " +
                               "Drag the Solution GameObject into the Runner field.");
                enabled = false;
                return;
            }

            runner.OnPoseLandmarkerOutput += OnResult;
            Debug.Log("[PoseDriver] Connected to PoseLandmarkerRunner. Ready.");
        }

        void OnDestroy()
        {
            if (runner != null)
                runner.OnPoseLandmarkerOutput -= OnResult;
        }

        // ── Callback (may be background thread in LIVE_STREAM mode) ──────────

        private void OnResult(PoseLandmarkerResult result)
        {
            lock (_lock)
            {
                _latestResult = result;
                _hasNewResult = true;
            }
        }

        // ── Main Thread ───────────────────────────────────────────────────────

        void LateUpdate()
        {
            PoseLandmarkerResult result;
            bool hasNew;
            lock (_lock)
            {
                result = _latestResult;
                hasNew = _hasNewResult;
                _hasNewResult = false;
            }

            if (!hasNew) return;
            if (result.poseLandmarks == null || result.poseLandmarks.Count == 0) return;

            var landmarks = result.poseLandmarks[0];
            if (landmarks.landmarks == null || landmarks.landmarks.Count < 29) return;

            Smooth(landmarks);
            ApplyBodyScale();
            DriveSpine();
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (!_visible[IDX_L_WRIST] && !_visible[IDX_R_WRIST] &&
                !_visible[IDX_L_ANKLE] && !_visible[IDX_R_ANKLE]) return;

            HandIK(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, IDX_L_WRIST, IDX_L_ELBOW);
            HandIK(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, IDX_R_WRIST, IDX_R_ELBOW);
            FootIK(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, IDX_L_ANKLE, IDX_L_KNEE);
            FootIK(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, IDX_R_ANKLE, IDX_R_KNEE);
        }

        // ── Coordinate Conversion ─────────────────────────────────────────────
        //
        // KEY CHANGE FROM PREVIOUS VERSION:
        //
        // Old approach:  arbitrary_world_pos = (lm.x - 0.5) * poseScale
        //   → character had no relationship to camera or screen
        //
        // New approach:  camera.ScreenToWorldPoint(screen_pixel, depth)
        //   → landmark is unprojected through the real camera frustum
        //   → character naturally overlays the person at any camera angle/FOV
        //
        // MediaPipe coords:  (0,0) = top-left,     Y increases downward
        // Unity screen coords: (0,0) = bottom-left, Y increases upward
        // → flip Y:  unityY = (1 - mediapipeY) * screenHeight

        private Vector3 ToWorld(NormalizedLandmark lm)
        {
            float sx = lm.x * cam.pixelWidth;
            float sy = (1f - lm.y) * cam.pixelHeight;
            return cam.ScreenToWorldPoint(new Vector3(sx, sy, characterDepth))
                   + worldOffset;
        }

        // ── Smoothing ─────────────────────────────────────────────────────────

        private void Smooth(NormalizedLandmarks landmarks)
        {
            var lms = landmarks.landmarks;
            for (int i = 0; i < Mathf.Min(lms.Count, 33); i++)
            {
                var lm = lms[i];
                _visible[i] = lm.visibility.GetValueOrDefault(0f) >= visibilityThreshold;
                if (_visible[i])
                    _smoothed[i] = Vector3.Lerp(ToWorld(lm), _smoothed[i], smoothing);
            }
        }

        // ── Dynamic Body Scale ────────────────────────────────────────────────
        //
        // The character model has bone lengths baked in at a fixed scale.
        // When the tracked person is close to the camera they appear larger;
        // when far away, smaller. ScreenToWorldPoint handles position but the
        // model bones stay a fixed length — so we scale the whole character
        // to match the observed shoulder span of the tracked person.
        //
        // shoulder span is used because:
        //   • it's almost always visible (high landmark confidence)
        //   • it's roughly constant regardless of pose
        //   • it's a reliable proxy for overall body size

        private void ApplyBodyScale()
        {
            if (!_visible[IDX_L_SHOULDER] || !_visible[IDX_R_SHOULDER]) return;

            // Observed shoulder span in world space (from camera projection)
            float observedSpan = Vector3.Distance(
              _smoothed[IDX_L_SHOULDER], _smoothed[IDX_R_SHOULDER]);

            // How much to scale the model so its shoulders match the tracked person
            float s = (observedSpan / _modelShoulderSpan) * scaleMultiplier;
            transform.localScale = Vector3.one * s;
        }

        // ── Spine & Head ──────────────────────────────────────────────────────

        private void DriveSpine()
        {
            if (!_visible[IDX_L_HIP] || !_visible[IDX_R_HIP] ||
                !_visible[IDX_L_SHOULDER] || !_visible[IDX_R_SHOULDER]) return;

            var lHip = _smoothed[IDX_L_HIP];
            var rHip = _smoothed[IDX_R_HIP];
            var lShoulder = _smoothed[IDX_L_SHOULDER];
            var rShoulder = _smoothed[IDX_R_SHOULDER];

            var hipCenter = (lHip + rHip) * 0.5f;
            var shoulderCenter = (lShoulder + rShoulder) * 0.5f;
            var spineUp = (shoulderCenter - hipCenter).normalized;

            // Place character root at hip centre in world space
            transform.position = hipCenter;

            // Hips rotation
            var hipRight = (rHip - lHip).normalized;
            var hipFwd = Vector3.Cross(hipRight, spineUp).normalized;
            var hipRot = Quaternion.LookRotation(hipFwd, spineUp);
            if (_hips != null) _hips.rotation = hipRot * _hipsRest;

            // Chest rotation
            var shoulderRight = (rShoulder - lShoulder).normalized;
            var chestFwd = Vector3.Cross(shoulderRight, spineUp).normalized;
            var chestRot = Quaternion.LookRotation(chestFwd, spineUp);

            // Distribute tilt gradually up the spine bones
            if (_spine != null) _spine.rotation =
              Quaternion.Slerp(hipRot, chestRot, 0.33f) * _spineRest;
            if (_chest != null) _chest.rotation =
              Quaternion.Slerp(hipRot, chestRot, 0.66f) * _chestRest;
            if (_upperChest != null) _upperChest.rotation =
              chestRot * _upperChestRest;

            DriveHead(chestRot, chestFwd);
        }

        private void DriveHead(Quaternion chestRot, Vector3 chestFwd)
        {
            if (!_visible[IDX_NOSE] ||
                !_visible[IDX_L_SHOULDER] || !_visible[IDX_R_SHOULDER]) return;

            var shoulderCenter = (_smoothed[IDX_L_SHOULDER] + _smoothed[IDX_R_SHOULDER]) * 0.5f;
            var headUp = (_smoothed[IDX_NOSE] - shoulderCenter).normalized;
            var headFwd = Vector3.ProjectOnPlane(chestFwd, headUp).normalized;
            if (headFwd.sqrMagnitude < 0.001f) headFwd = chestFwd;

            var headRot = Quaternion.LookRotation(headFwd, headUp);
            if (_neck != null) _neck.rotation = headRot * _neckRest;
            if (_head != null) _head.rotation = headRot;
        }

        // ── IK ────────────────────────────────────────────────────────────────

        private void HandIK(AvatarIKGoal goal, AvatarIKHint hint, int endIdx, int hintIdx)
        {
            float w = _visible[endIdx] ? handIKWeight : 0f;
            _animator.SetIKPositionWeight(goal, w);
            _animator.SetIKRotationWeight(goal, w);
            if (_visible[endIdx]) _animator.SetIKPosition(goal, _smoothed[endIdx]);

            float hw = _visible[hintIdx] ? hintIKWeight : 0f;
            _animator.SetIKHintPositionWeight(hint, hw);
            if (_visible[hintIdx]) _animator.SetIKHintPosition(hint, _smoothed[hintIdx]);
        }

        private void FootIK(AvatarIKGoal goal, AvatarIKHint hint, int endIdx, int hintIdx)
        {
            float w = _visible[endIdx] ? footIKWeight : 0f;
            _animator.SetIKPositionWeight(goal, w);
            _animator.SetIKRotationWeight(goal, w * 0.5f);
            if (_visible[endIdx]) _animator.SetIKPosition(goal, _smoothed[endIdx]);

            float hw = _visible[hintIdx] ? hintIKWeight : 0f;
            _animator.SetIKHintPositionWeight(hint, hw);
            if (_visible[hintIdx]) _animator.SetIKHintPosition(hint, _smoothed[hintIdx]);
        }

        // ── Bone Cache ────────────────────────────────────────────────────────

        private void CacheBones()
        {
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _lShoulderBone = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rShoulderBone = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);

            if (_hips != null) _hipsRest = _hips.localRotation;
            if (_spine != null) _spineRest = _spine.localRotation;
            if (_chest != null) _chestRest = _chest.localRotation;
            if (_upperChest != null) _upperChestRest = _upperChest.localRotation;
            if (_neck != null) _neckRest = _neck.localRotation;

            // Measure the model's shoulder span in T-pose at scale=1 so we can
            // compute the correct per-frame body scale in ApplyBodyScale()
            if (_lShoulderBone != null && _rShoulderBone != null)
            {
                // Temporarily reset scale to 1 so measurement is in model units
                var prevScale = transform.localScale;
                transform.localScale = Vector3.one;
                _modelShoulderSpan = Vector3.Distance(
                  _lShoulderBone.position, _rShoulderBone.position);
                transform.localScale = prevScale;

                if (_modelShoulderSpan < 0.001f)
                {
                    _modelShoulderSpan = 0.5f; // safe fallback
                    Debug.LogWarning("[PoseDriver] Shoulder span is near zero — " +
                                     "check that the Avatar is configured correctly.");
                }
                else
                {
                    Debug.Log($"[PoseDriver] Model shoulder span: {_modelShoulderSpan:F3}m");
                }
            }

            if (_hips == null) Debug.LogWarning("[PoseDriver] Hips not found — check Avatar.");
            if (_spine == null) Debug.LogWarning("[PoseDriver] Spine not found — check Avatar.");
        }

        // ── Gizmos ────────────────────────────────────────────────────────────

        void OnDrawGizmos()
        {
            if (!drawDebugGizmos || !Application.isPlaying) return;

            foreach (int i in new[] {
        IDX_NOSE, IDX_L_SHOULDER, IDX_R_SHOULDER, IDX_L_ELBOW, IDX_R_ELBOW,
        IDX_L_WRIST, IDX_R_WRIST, IDX_L_HIP, IDX_R_HIP,
        IDX_L_KNEE, IDX_R_KNEE, IDX_L_ANKLE, IDX_R_ANKLE })
            {
                if (!_visible[i]) continue;
                Gizmos.color = i % 2 == 0 ? Color.cyan : Color.yellow;
                Gizmos.DrawSphere(_smoothed[i], 0.02f);
            }

            Gizmos.color = Color.green;
            void Bone(int a, int b)
            {
                if (_visible[a] && _visible[b])
                    Gizmos.DrawLine(_smoothed[a], _smoothed[b]);
            }
            Bone(IDX_L_SHOULDER, IDX_R_SHOULDER); Bone(IDX_L_HIP, IDX_R_HIP);
            Bone(IDX_L_SHOULDER, IDX_L_ELBOW); Bone(IDX_L_ELBOW, IDX_L_WRIST);
            Bone(IDX_R_SHOULDER, IDX_R_ELBOW); Bone(IDX_R_ELBOW, IDX_R_WRIST);
            Bone(IDX_L_HIP, IDX_L_KNEE); Bone(IDX_L_KNEE, IDX_L_ANKLE);
            Bone(IDX_R_HIP, IDX_R_KNEE); Bone(IDX_R_KNEE, IDX_R_ANKLE);
        }
    }
}