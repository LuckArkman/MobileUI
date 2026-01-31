using UnityEngine;
using System.Collections.Generic;

namespace ARProject.Heatmap
{
    /// <summary>
    /// Controls the generation and visualization of the proximity heatmap.
    /// Colors: Blue (Safe) -> Green -> Yellow -> Red (Critical)
    /// </summary>
    public class HeatmapController : MonoBehaviour
    {
        [SerializeField] private Material heatmapMaterial;

        [SerializeField] private HeatmapShaderDriver shaderDriver;
        [SerializeField] private float intensityFalloff = 1.0f;

        // Data storage
        private List<Vector3> activeDangerPoints = new List<Vector3>();
        private List<float> activeIntensities = new List<float>();

        // Placeholder for shader property IDs
        private static readonly int HeatmapPointsID = Shader.PropertyToID("_HeatmapPoints");

        public void AddDangerPoint(Vector3 position, float intensity)
        {
            activeDangerPoints.Add(position);
            activeIntensities.Add(intensity);

            // Limit list size if needed or handle cleanup
            if (activeDangerPoints.Count > 20)
            {
                activeDangerPoints.RemoveAt(0);
                activeIntensities.RemoveAt(0);
            }

            RefreshShader();
        }

        // Call this every frame or when data changes
        private void RefreshShader()
        {
            if (shaderDriver != null)
            {
                shaderDriver.UpdateShaderData(activeDangerPoints.ToArray(), activeIntensities.ToArray());
            }
        }

        public void UpdateHeatmap(Vector3 userPosition, List<Vector3> dangerPoints)
        {
            // Reset and load new batch
            activeDangerPoints.Clear();
            activeIntensities.Clear();

            foreach (var point in dangerPoints)
            {
                float dist = Vector3.Distance(userPosition, point);
                float intensity = Mathf.Clamp01(1.0f / (dist * intensityFalloff));

                activeDangerPoints.Add(point);
                activeIntensities.Add(intensity);
            }

            RefreshShader();
        }

        public Color GetColorForIntensity(float intensity)
        {
            // Simple gradient logic
            return Color.Lerp(Color.blue, Color.red, intensity);
        }
    }
}
