using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace MicroScenes
{
    public class MicroSceneManager : MonoBehaviour
    {
        public GameObject microScenePrefab;

        public void CreateMicroScene(Pose pose)
        {
            var anchorGO = new GameObject("MicroSceneAnchor");
            var anchor = anchorGO.AddComponent<ARAnchor>();
            anchorGO.transform.SetPositionAndRotation(pose.position, pose.rotation);

            var scene = Instantiate(microScenePrefab);
            scene.GetComponent<MicroSceneAnchor>().Attach(anchor);
        }
    }

}