using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>WoW-style orbit camera: follows a target, right-mouse drag rotates,
    /// scroll wheel zooms, simple collision-free rig for the gray-box phase.
    /// Purely local — never networked.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        [SerializeField] private float distance = 8f;
        [SerializeField] private float minDistance = 2.5f;
        [SerializeField] private float maxDistance = 16f;
        [SerializeField] private float sensitivity = 3.5f;
        [SerializeField] private float pitchMin = -30f;
        [SerializeField] private float pitchMax = 75f;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);

        private Transform _target;
        private float _yaw;
        private float _pitch = 25f;

        public float Yaw => _yaw;

        public void SetTarget(Transform target) => _target = target;

        private void LateUpdate()
        {
            if (_target == null) return;

            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * sensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * sensitivity;
                _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
                distance = Mathf.Clamp(distance - scroll * 4f, minDistance, maxDistance);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focus = _target.position + targetOffset;
            transform.position = focus - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
        }
    }
}
