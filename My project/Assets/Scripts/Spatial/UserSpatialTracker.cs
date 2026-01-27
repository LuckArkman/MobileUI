using UnityEngine;

namespace Spatial
{
    using UnityEngine;

    public class UserSpatialTracker : MonoBehaviour
    {
        public static UserSpatialTracker Instance;
        public Transform CameraTransform { get; private set; }

        void Awake()
        {
            Instance = this;
            CameraTransform = Camera.main.transform;
        }

        public Vector3 Position => CameraTransform.position;
    }

}