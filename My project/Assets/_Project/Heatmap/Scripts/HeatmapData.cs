using UnityEngine;

namespace ARProject.Heatmap
{
    [System.Serializable]
    public class HeatmapData
    {
        public float Intensity; // 0 to 1
        public Vector3 WorldPosition;
    }
}
