using Spatial;
using UnityEngine;

namespace Heatmap
{
    public class HeatmapShaderDriver : MonoBehaviour
    {
        public Material material;
        public HeatmapController heatmap;

        void Update()
        {
            float d = Vector3.Distance(
                UserSpatialTracker.Instance.Position,
                transform.position
            );

            material.SetColor("_HeatColor", heatmap.Evaluate(d));
        }
    }

}