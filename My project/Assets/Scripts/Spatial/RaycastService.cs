using UnityEngine;

namespace Spatial
{
    using UnityEngine;
    using UnityEngine.XR.ARFoundation;
    using System.Collections.Generic;

    public class RaycastService : MonoBehaviour
    {
        public ARRaycastManager raycastManager;
        static List<ARRaycastHit> hits = new();

        public bool Raycast(Vector2 screenPos, out Pose pose)
        {
            if (raycastManager.Raycast(screenPos, hits))
            {
                pose = hits[0].pose;
                return true;
            }
            pose = default;
            return false;
        }
    }

}