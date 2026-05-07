using UnityEngine;

namespace Mediapipe.Unity.Sample.PoseLandmarkDetection
{
    using Mediapipe.Tasks.Vision.PoseLandmarker;
    using Mediapipe.Tasks.Vision.FaceLandmarker;
    using Mediapipe.Unity.Sample.FaceLandmarkDetection;
    using Mediapipe.Tasks.Components.Containers;

    [RequireComponent(typeof(Animator))]
    public class MediaPipePoseDriver : MonoBehaviour
    {
        [Header("Connection")]
        public PoseLandmarkerRunner runner;
        public FaceLandmarkerRunner faceRunner;

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

        // Pose indices
        private const int IDX_L_SHOULDER = 11;
        private const int IDX_R_SHOULDER = 12;
        private const int IDX_L_ELBOW = 13;
        private const int IDX_R_ELBOW = 14;
        private const int IDX_L_WRIST = 15;
        private const int IDX_R_WRIST = 16;
        private const int IDX_L_KNEE = 25;
        private const int IDX_R_KNEE = 26;
        private const int IDX_L_ANKLE = 27;
        private const int IDX_R_ANKLE = 28;

        // Face indices
        private const int IDX_FACE_NOSE = 1;
        private const int IDX_FACE_L_TEMPLE = 234;
        private const int IDX_FACE_R_TEMPLE = 454;

        private Animator _animator;

        private PoseLandmarkerResult _latestPoseResult;
        private FaceLandmarkerResult _latestFaceResult;

        private bool _hasNewPoseResult;
        private bool _hasNewFaceResult;
        private bool _hasFaceResult;

        private readonly object _lock = new object();

        private readonly Vector3[] _smoothed = new Vector3[33];
        private readonly bool[] _visible = new bool[33];

        private float _modelShoulderSpan = 1f;
        private float _shoulderToRootOffset;

        // 🔥 Look-at tracking instead of direct head rotation
        private Vector3 _lookTarget;
        private bool _lookInitialized = false;

        void Awake()
        {
            _animator = GetComponent<Animator>();
            if (cam == null) cam = Camera.main;

            var leftShoulder = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rightShoulder = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);

            if (leftShoulder != null && rightShoulder != null)
                _modelShoulderSpan = Vector3.Distance(leftShoulder.position, rightShoulder.position);

            if (_modelShoulderSpan < 0.001f)
                _modelShoulderSpan = 0.5f;

            if (leftShoulder != null && hips != null)
                _shoulderToRootOffset = hips.position.y - leftShoulder.position.y;
        }

        void Start()
        {
            if (runner == null)
                runner = FindObjectOfType<PoseLandmarkerRunner>();
            runner.OnPoseLandmarkerOutput += OnPoseResult;

            if (faceRunner == null)
                faceRunner = FindObjectOfType<FaceLandmarkerRunner>();

            if (faceRunner != null)
                faceRunner.OnFaceLandmarkerOutput += OnFaceResult;
        }

        void OnDestroy()
        {
            if (runner != null)
                runner.OnPoseLandmarkerOutput -= OnPoseResult;

            if (faceRunner != null)
                faceRunner.OnFaceLandmarkerOutput -= OnFaceResult;
        }

        private void OnPoseResult(PoseLandmarkerResult result)
        {
            lock (_lock)
            {
                _latestPoseResult = result;
                _hasNewPoseResult = true;
            }
        }

        private void OnFaceResult(FaceLandmarkerResult result)
        {
            lock (_lock)
            {
                _latestFaceResult = result;
                _hasNewFaceResult = true;
                _hasFaceResult = true;
            }
        }

        void LateUpdate()
        {
            PoseLandmarkerResult poseResult;
            FaceLandmarkerResult faceResult;
            bool hasNewPose, hasNewFace, hasFace;

            lock (_lock)
            {
                poseResult = _latestPoseResult;
                faceResult = _latestFaceResult;
                hasNewPose = _hasNewPoseResult;
                hasNewFace = _hasNewFaceResult;
                hasFace = _hasFaceResult;

                _hasNewPoseResult = false;
                _hasNewFaceResult = false;
            }

            if (!hasNewPose || poseResult.poseLandmarks == null || poseResult.poseLandmarks.Count == 0)
                return;

            var landmarks = poseResult.poseLandmarks[0];
            if (landmarks.landmarks == null) return;

            Smooth(landmarks);
            ApplyBodyScale();
            DriveSpine();

            if (hasFace && faceResult.faceLandmarks != null && faceResult.faceLandmarks.Count > 0)
                UpdateLookTarget(faceResult.faceLandmarks[0]);
        }

        void OnAnimatorIK(int layerIndex)
        {
            HandIK(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, IDX_L_WRIST, IDX_L_ELBOW);
            HandIK(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, IDX_R_WRIST, IDX_R_ELBOW);
            FootIK(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, IDX_L_ANKLE, IDX_L_KNEE);
            FootIK(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, IDX_R_ANKLE, IDX_R_KNEE);

            ApplyLookAt(); // 🔥 KEY FIX
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

            float observedSpan = Vector3.Distance(_smoothed[IDX_L_SHOULDER], _smoothed[IDX_R_SHOULDER]);
            float s = (observedSpan / _modelShoulderSpan) * scaleMultiplier;

            transform.localScale = Vector3.one * s;
        }

        private void DriveSpine()
        {
            if (!_visible[IDX_L_SHOULDER] || !_visible[IDX_R_SHOULDER]) return;

            var center = (_smoothed[IDX_L_SHOULDER] + _smoothed[IDX_R_SHOULDER]) * 0.5f;
            transform.position = center + new Vector3(0f, _shoulderToRootOffset, 0f);
        }

        private void UpdateLookTarget(NormalizedLandmarks faceLandmarks)
        {
            var lms = faceLandmarks.landmarks;
            if (lms == null || lms.Count <= IDX_FACE_R_TEMPLE) return;

            Vector3 left = ToWorld(lms[IDX_FACE_L_TEMPLE]);
            Vector3 right = ToWorld(lms[IDX_FACE_R_TEMPLE]);
            Vector3 nose = ToWorld(lms[IDX_FACE_NOSE]);

            Vector3 faceRight = (right - left).normalized;
            if (faceRight.sqrMagnitude < 0.001f) return;

            Vector3 faceForward = Vector3.Cross(faceRight, Vector3.up).normalized;

            Vector3 center = (left + right) * 0.5f;
            Vector3 noseOffset = (nose - center) * 0.5f;

            Vector3 finalForward = (faceForward + noseOffset).normalized;

            _lookTarget = center + finalForward * 10f;
            _lookInitialized = true;
        }

        private void ApplyLookAt()
        {
            if (!_lookInitialized) return;

            _animator.SetLookAtWeight(1.0f, 0.0f, 0.9f, 1.0f, 0.3f);
            _animator.SetLookAtPosition(_lookTarget);
        }

        private void HandIK(AvatarIKGoal goal, AvatarIKHint hint, int endIdx, int hintIdx)
        {
            float w = _visible[endIdx] ? handIKWeight : 0f;
            _animator.SetIKPositionWeight(goal, w);

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

            if (_visible[endIdx])
                _animator.SetIKPosition(goal, _smoothed[endIdx]);

            float hw = _visible[hintIdx] ? hintIKWeight : 0f;
            _animator.SetIKHintPositionWeight(hint, hw);

            if (_visible[hintIdx])
                _animator.SetIKHintPosition(hint, _smoothed[hintIdx]);
        }
    }
}