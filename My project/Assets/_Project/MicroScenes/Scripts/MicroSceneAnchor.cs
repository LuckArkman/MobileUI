using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARProject.MicroScenes
{
    /// <summary>
    /// Helper component to handle AR Anchor updates for the scene parent.
    /// </summary>
    [RequireComponent(typeof(ARAnchor))]
    public class MicroSceneAnchor : MonoBehaviour
    {
        private ARAnchor arAnchor;

        private void Awake()
        {
            arAnchor = GetComponent<ARAnchor>();
        }

        // Potential logic to handle tracking loss or restoration
    }
}
