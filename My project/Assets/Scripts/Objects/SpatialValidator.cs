using UnityEngine;

namespace Objects
{
    public class SpatialValidator
    {
        public static bool IsPositionValid(Vector3 position, float radius)
        {
            return Physics.OverlapSphere(position, radius).Length == 0;
        }
    }
}