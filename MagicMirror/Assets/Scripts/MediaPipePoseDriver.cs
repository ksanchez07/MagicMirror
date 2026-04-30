using UnityEngine;

namespace Mediapipe.Unity.Sample.PoseLandmarkDetection
{
    using Mediapipe.Tasks.Vision.PoseLandmarker;
    using Mediapipe.Tasks.Components.Containers;

    [RequireComponent(typeof(Animator))]
    public class MediaPipePoseDriver : MonoBehaviour
    {
        [Header("Connection")]
        public PoseLandmarkerRunner runner;

        [Header("Camera & Depth")]
        public Camera cam;
        public float characterDepth = 2f;

        [Header("Smoothing")]
        [Range(0f, 0.99f)]
        public float smoothing = 0.85f;

        [Header("Fine Tuning")]
        public Vector3 worldOffset = Vector3.zero;
        public float scaleMultiplier = 1f;

        [Header("IK Weights")]
        [Range(0f, 1f)] public float handIKWeight = 1.0f;
        [Range(0f, 1f)] public float footIKWeight = 1.0f;
        [Range(0f, 1f)] public float hintIKWeight = 0.6f;

        [Header("Confidence Threshold")]
        [Range(0f, 1f)]
        public float visibilityThreshold = 0.5f;

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
        private const int IDX_L_EAR = 7;
        private const int IDX_R_EAR = 8;

        private Animator _animator;
        private PoseLandmarkerResult _latestResult;
        private bool _hasNewResult;
        private readonly object _lock = new object();

        private readonly Vector3[] _smoothed = new Vector3[33];
        private readonly bool[] _visible = new bool[33];

        private float _modelShoulderSpan = 1f;

        void Awake()
        {
            _animator = GetComponent<Animator>();
            if (cam == null) cam = Camera.main;

            var l = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var r = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);

            if (l != null && r != null)
                _modelShoulderSpan = Vector3.Distance(l.position, r.position);

            if (_modelShoulderSpan < 0.001f)
                _modelShoulderSpan = 0.5f;
        }

        void Start()
        {
            if (runner == null)
                runner = FindObjectOfType<PoseLandmarkerRunner>();

            runner.OnPoseLandmarkerOutput += OnResult;
        }

        void OnDestroy()
        {
            if (runner != null)
                runner.OnPoseLandmarkerOutput -= OnResult;
        }

        private void OnResult(PoseLandmarkerResult result)
        {
            lock (_lock)
            {
                _latestResult = result;
                _hasNewResult = true;
            }
        }

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
            if (landmarks.landmarks == null) return;

            Smooth(landmarks);
            ApplyBodyScale();
            DriveSpine();
            DriveHead(transform.rotation, transform.forward);
        }

        void OnAnimatorIK(int layerIndex)
        {
            HandIK(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, IDX_L_WRIST, IDX_L_ELBOW);
            HandIK(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, IDX_R_WRIST, IDX_R_ELBOW);
            FootIK(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, IDX_L_ANKLE, IDX_L_KNEE);
            FootIK(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, IDX_R_ANKLE, IDX_R_KNEE);
        }

        private Vector3 ToWorld(NormalizedLandmark lm)
        {
            float sx = lm.x * cam.pixelWidth;
            float sy = (1f - lm.y) * cam.pixelHeight;

            return cam.ScreenToWorldPoint(new Vector3(sx, sy, characterDepth)) + worldOffset;
        }

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

        private void ApplyBodyScale()
        {
            if (!_visible[IDX_L_SHOULDER] || !_visible[IDX_R_SHOULDER]) return;

            float observedSpan = Vector3.Distance(
                _smoothed[IDX_L_SHOULDER],
                _smoothed[IDX_R_SHOULDER]);

            float s = (observedSpan / _modelShoulderSpan) * scaleMultiplier;
            transform.localScale = Vector3.one * s;
        }

        private void DriveSpine()
        {
            if (!_visible[IDX_L_SHOULDER] || !_visible[IDX_R_SHOULDER]) return;

            var lShoulder = _smoothed[IDX_L_SHOULDER];
            var rShoulder = _smoothed[IDX_R_SHOULDER];

            // Use shoulders because they are visible more often than hips
            var shoulderCenter = (lShoulder + rShoulder) * 0.5f;

            // Move character down from shoulders to where hips/root should be
            transform.position = shoulderCenter + new Vector3(0f, -0.65f * transform.localScale.y, 0f);
        }

        private void DriveHead(Quaternion baseRot, Vector3 forward)
        {
            if (!_visible[IDX_NOSE] || !_visible[IDX_L_EAR] || !_visible[IDX_R_EAR] ||
                !_visible[IDX_L_SHOULDER] || !_visible[IDX_R_SHOULDER]) return;

            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;

            Vector3 nose = _smoothed[IDX_NOSE];
            Vector3 leftEar = _smoothed[IDX_L_EAR];
            Vector3 rightEar = _smoothed[IDX_R_EAR];

            Vector3 earCenter = (leftEar + rightEar) * 0.5f;
            Vector3 headRight = (rightEar - leftEar).normalized;
            Vector3 headForward = (nose - earCenter).normalized;

            if (headForward.sqrMagnitude < 0.001f)
                return;

            Vector3 headUp = Vector3.Cross(headForward, headRight).normalized;

            if (headUp.sqrMagnitude < 0.001f)
                headUp = Vector3.up;

            Quaternion targetRot = Quaternion.LookRotation(headForward, headUp);

            // Smooth movement so it does not twitch
            head.rotation = Quaternion.Slerp(head.rotation, targetRot, 0.25f);
        }

        private void HandIK(AvatarIKGoal goal, AvatarIKHint hint, int endIdx, int hintIdx)
        {
            float w = _visible[endIdx] ? handIKWeight : 0f;

            _animator.SetIKPositionWeight(goal, w);
            _animator.SetIKRotationWeight(goal, 0f); 

            if (_visible[endIdx])
                _animator.SetIKPosition(goal, _smoothed[endIdx]);

            float hw = _visible[hintIdx] ? hintIKWeight : 0f;

            _animator.SetIKHintPositionWeight(hint, hw);

            if (_visible[hintIdx])
                _animator.SetIKHintPosition(hint, _smoothed[hintIdx]);
        }

        private void FootIK(AvatarIKGoal goal, AvatarIKHint hint, int endIdx, int hintIdx)
        {
            float w = _visible[endIdx] ? footIKWeight : 0f;

            _animator.SetIKPositionWeight(goal, w);
            _animator.SetIKRotationWeight(goal, 0f);

            if (_visible[endIdx])
                _animator.SetIKPosition(goal, _smoothed[endIdx]);

            float hw = _visible[hintIdx] ? hintIKWeight : 0f;

            _animator.SetIKHintPositionWeight(hint, hw);

            if (_visible[hintIdx])
                _animator.SetIKHintPosition(hint, _smoothed[hintIdx]);
        }
    }
}