using UnityEngine;
using System.Collections.Generic;

namespace LuckArkman.XR.Safety
{
    public class HeatmapManager : MonoBehaviour
    {
        [Header("Configurações do Heatmap")]
        [Tooltip("Ative para visualizar o heatmap na tela. Desative em produção para poupar bateria.")]
        public bool enableHeatmap = false; 

        public Material heatmapMaterial;
        public int maxPoints = 50;

        private Vector4[] points;

        public struct HeatmapPoint
        {
            public float x;
            public float y;
            public float size;
            public float riskScore;
        }

        private void Start()
        {
            points = new Vector4[maxPoints];
        }

        public void UpdateHeatmap(List<HeatmapPoint> currentPoints)
        {
            if (!enableHeatmap)
            {
                Shader.SetGlobalInt("_HeatmapCount", 0);
                return;
            }

            for (int i = 0; i < maxPoints; i++)
            {
                if (i < currentPoints.Count)
                {
                    points[i] = new Vector4(
                        currentPoints[i].x, 
                        currentPoints[i].y, 
                        currentPoints[i].size, 
                        currentPoints[i].riskScore
                    );
                }
                else
                {
                    points[i] = Vector4.zero;
                }
            }

            Shader.SetGlobalVectorArray("_HeatmapPoints", points);
            Shader.SetGlobalInt("_HeatmapCount", Mathf.Min(currentPoints.Count, maxPoints));
        }
    }
}