using UnityEngine;

namespace Heatmap
{
    using UnityEngine;

    public class HeatmapController : MonoBehaviour
    {
        public Gradient gradient;
        public float maxDistance = 3f;

        public Color Evaluate(float distance)
        {
            float t = Mathf.Clamp01(distance / maxDistance);
            return gradient.Evaluate(1f - t);
        }
    }

}