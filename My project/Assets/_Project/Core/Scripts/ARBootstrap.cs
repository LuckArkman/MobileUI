using UnityEngine;
using UnityEngine.SceneManagement;

namespace ARProject.Core
{
    /// <summary>
    /// Entry point for the AR Application. Handles initialization and permission requests.
    /// </summary>
    public class ARBootstrap : MonoBehaviour
    {
        [SerializeField] private bool autoStartAR = true;

        private void Start()
        {
            InitializeApp();
        }

        private void InitializeApp()
        {
            Debug.Log("[ARBootstrap] Initializing Application...");

            // Check permissions here (Camera, Location if needed)
            // For now we assume Unity handles basic Camera permissions via Android Manifest

            if (autoStartAR)
            {
                StartARSession();
            }
        }

        public void StartARSession()
        {
            Debug.Log("[ARBootstrap] Starting AR Session...");
            // Load main AR Scene or enable AR components
        }
    }
}
