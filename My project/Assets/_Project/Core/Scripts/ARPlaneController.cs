using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARProject.Core
{
    /// <summary>
    /// Manages AR Plane detection visualization and logic.
    /// </summary>
    public class ARPlaneController : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private GameObject planePrefab;

        private void Awake()
        {
            if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
        }

        private void OnEnable()
        {
            if (planeManager != null)
                planeManager.planesChanged += OnPlanesChanged;
        }

        private void OnDisable()
        {
            if (planeManager != null)
                planeManager.planesChanged -= OnPlanesChanged;
        }

        private void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            // Handle added/updated/removed planes
            foreach (var plane in args.added)
            {
                // Custom logic for new planes (e.g., enable heatmap on them)
                SetupPlane(plane);
            }
        }

        private void SetupPlane(ARPlane plane)
        {
            // potentially attach a heatmap receiver
        }

        public void TogglePlaneDetection(bool enable)
        {
            if (planeManager != null)
            {
                planeManager.enabled = enable;
                foreach (var plane in planeManager.trackables)
                {
                    plane.gameObject.SetActive(enable);
                }
            }
        }
    }
}
