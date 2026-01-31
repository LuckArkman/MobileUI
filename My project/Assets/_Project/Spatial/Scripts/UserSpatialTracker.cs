using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARProject.Spatial
{
    /// <summary>
    /// Tracks the user's position and orientation in real-world space.
    /// Acts as the "Player" avatar representation.
    /// </summary>
    public class UserSpatialTracker : MonoBehaviour
    {
        [SerializeField] private Camera arCamera;

        public Vector3 CurrentPosition => arCamera != null ? arCamera.transform.position : Vector3.zero;
        public Quaternion CurrentRotation => arCamera != null ? arCamera.transform.rotation : Quaternion.identity;
        public Vector3 Forward => arCamera != null ? arCamera.transform.forward : Vector3.forward;

        private void Awake()
        {
            if (arCamera == null) arCamera = Camera.main;
        }

        private void Update()
        {
            // Logic to update user state or trigger events based on movement
        }
    }
}
