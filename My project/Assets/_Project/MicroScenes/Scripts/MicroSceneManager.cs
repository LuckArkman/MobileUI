using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ARProject.Spatial;

namespace ARProject.MicroScenes
{
    /// <summary>
    /// Manages the placement and lifecycle of MicroScenes in the world.
    /// </summary>
    public class MicroSceneManager : MonoBehaviour
    {
        [SerializeField] private MicroScene microScenePrefab;
        [SerializeField] private RaycastService raycastService;
        [SerializeField] private ARAnchorManager anchorManager;

        private List<MicroScene> activeScenes = new List<MicroScene>();

        public void SpawnSceneAt(Pose pose)
        {
            if (microScenePrefab == null) return;

            // Create Anchor manually (AR Foundation 6+ style)
            GameObject anchorGO = new GameObject("MicroScene_Anchor");
            anchorGO.transform.position = pose.position;
            anchorGO.transform.rotation = pose.rotation;

            ARAnchor anchor = anchorGO.AddComponent<ARAnchor>();

            if (anchor != null)
            {
                MicroScene newScene = Instantiate(microScenePrefab, anchor.transform);
                newScene.transform.localPosition = Vector3.zero;
                newScene.transform.localRotation = Quaternion.identity;
                activeScenes.Add(newScene);
                Debug.Log("[MicroSceneManager] Scene spawned attached to anchor.");
            }
        }
    }
}
