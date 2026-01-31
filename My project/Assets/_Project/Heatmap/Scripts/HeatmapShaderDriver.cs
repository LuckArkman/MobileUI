using UnityEngine;

namespace ARProject.Heatmap
{
    public class HeatmapShaderDriver : MonoBehaviour
    {
        [SerializeField] private Material heatmapMaterial;

        [SerializeField] private int maxPoints = 20;

        private Vector4[] pointsArray;

        private void Awake()
        {
            pointsArray = new Vector4[maxPoints];
        }

        // Updates global shader variables for the Heatmap effect
        public void UpdateShaderData(Vector3[] points, float[] intensities)
        {
            int count = Mathf.Min(points.Length, maxPoints);

            for (int i = 0; i < count; i++)
            {
                // Pack position and intensity into Vector4 (x,y,z,w)
                pointsArray[i] = new Vector4(points[i].x, points[i].y, points[i].z, intensities[i]);
            }

            Shader.SetGlobalVectorArray("_HeatmapPoints", pointsArray);
            Shader.SetGlobalInt("_PointsCount", count);
        }
    }
}
