using UnityEngine;

namespace MicroScenes
{
    using UnityEngine.XR.ARFoundation;

    public class MicroSceneAnchor : MonoBehaviour
    {
        public ARAnchor anchor;

        public void Attach(ARAnchor newAnchor)
        {
            anchor = newAnchor;
            transform.SetParent(anchor.transform);
        }
    }

}