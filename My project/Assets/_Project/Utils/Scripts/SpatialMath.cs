using UnityEngine;

namespace ARProject.Utils
{
    public static class SpatialMath
    {
        /// <summary>
        /// Calculates the horizontal distance (ignoring Y axis) between two points.
        /// </summary>
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector2 p1 = new Vector2(a.x, a.z);
            Vector2 p2 = new Vector2(b.x, b.z);
            return Vector2.Distance(p1, p2);
        }

        /// <summary>
        /// Remaps a value from one range to another.
        /// </summary>
        public static float Remap(float value, float from1, float to1, float from2, float to2)
        {
            return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
        }

        /// <summary>
        /// Checks if a position is inside the 100x100 microscene bounds (centered at origin).
        /// </summary>
        public static bool IsWithinBounds(Vector3 localPosition, float size)
        {
            float halfSize = size / 2.0f;
            return Mathf.Abs(localPosition.x) <= halfSize && Mathf.Abs(localPosition.z) <= halfSize;
        }
    }
}
