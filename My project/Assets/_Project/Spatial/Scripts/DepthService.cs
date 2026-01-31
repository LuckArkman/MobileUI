using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARProject.Spatial
{
    /// <summary>
    /// Handles Depth API interactions to retrieve depth maps and occlusion data.
    /// </summary>
    public class DepthService : MonoBehaviour
    {
        [SerializeField] private AROcclusionManager occlusionManager;

        public bool IsDepthAvailable => occlusionManager != null && occlusionManager.subsystem != null && occlusionManager.subsystem.running;

        private void Awake()
        {
            if (occlusionManager == null) occlusionManager = FindFirstObjectByType<AROcclusionManager>();
        }

        public float GetDistanceToPixel(Vector2 screenPoint)
        {
            // Placeholder for raw depth sampling implementation
            // Requires CPU image access setup
            return -1f;
        }
    }
}
