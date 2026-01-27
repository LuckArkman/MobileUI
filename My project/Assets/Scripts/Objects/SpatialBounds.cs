using Spatial;
using UnityEngine;

namespace Objects
{
    public class SpatialBounds : MonoBehaviour
    {
        public float radius = 0.5f;

        public bool IsUserInside()
        {
            float dist = Vector3.Distance(
                UserSpatialTracker.Instance.Position,
                transform.position
            );
            return dist <= radius;
        }
    }

}