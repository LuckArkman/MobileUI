using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARProject.Core
{
    /// <summary>
    /// Manages the AR Session lifecycle and configuration.
    /// </summary>
    public class ARSessionController : MonoBehaviour
    {
        [SerializeField] private ARSession arSession;
        [SerializeField] private ARInputManager arInputManager;

        private void Awake()
        {
            if (arSession == null) arSession = FindFirstObjectByType<ARSession>();
            if (arInputManager == null) arInputManager = FindFirstObjectByType<ARInputManager>();
        }

        public void ResetSession()
        {
            if (arSession != null)
            {
                arSession.Reset();
            }
        }

        public void ToggleSession(bool paused)
        {
            if (arSession != null)
            {
                arSession.enabled = !paused;
            }
        }
    }
}
