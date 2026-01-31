using UnityEngine;

namespace ARProject.MicroScenes
{
    /// <summary>
    /// Represents a 100x100 unit MicroScene containing terrain and interactive objects.
    /// </summary>
    public class MicroScene : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private string sceneId;
        [SerializeField] private Vector3 dimensions = new Vector3(1, 1, 1); // Normalized size in AR

        public string SceneId => sceneId;

        private void Start()
        {
            InitializeScene();
        }

        private void InitializeScene()
        {
            // Setup bounds, colliders, initial state
            Debug.Log($"[MicroScene] Initialized {sceneId}");
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}
