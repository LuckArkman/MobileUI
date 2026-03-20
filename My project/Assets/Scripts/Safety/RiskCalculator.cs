using UnityEngine;
using System.Collections.Generic;
using LuckArkman.XR.AI; // Necessário para ler a saída do YOLO

namespace LuckArkman.XR.Safety
{
    /// <summary>
    /// Calcula o risco de colisão baseado em Visão Computacional 2D (La Rosa).
    /// Avalia a Extração, Bordas e Escala para decidir a direção de escape.
    /// </summary>
    public class RiskCalculator : MonoBehaviour
    {
        [Header("Parâmetros de Risco 2D")]
        // ANTES: public float safetyRadius = 1.0f; (Usava métrica de profundidade 3D em metros)
        // ANTES: public float cautionRadius = 2.5f;
        
        // AGORA: Usamos a proporção da tela (Escala). Se o objeto ocupa 60% da tela, é perigo!
        [Tooltip("Porcentagem da tela que o objeto deve ocupar para gerar colisão (0.0 a 1.0)")]
        public float dangerScaleThreshold = 0.6f; 

        // ANTES: public struct ObjectRiskProfile { public Vector3 position; ... }
        
        // AGORA: Estrutura alinhada com as caixas do Diagrama (Decisão e Comando HTTP)
        public struct SafetyDecision
        {
            public float riskScore;
            public int recommendedAngle; // 0, 90 ou 180 graus
            public string audioAlert;
            public bool hasObstacle;
        }

        /// <summary>
        /// Avalia a lista de detecções do YOLO e toma a decisão direcional.
        /// </summary>
        // ANTES: public float CalculateScore(Vector3 userPos, Vector3 objectPos, Vector3 objectVelocity)
        // AGORA: Lê a Bounding Box direta da IA e a resolução da câmera
        public SafetyDecision EvaluateSafety(List<DetectionResult> detections, int screenWidth, int screenHeight)
        {
            // Decisão padrão: Caminho Livre
            SafetyDecision decision = new SafetyDecision
            {
                riskScore = 0f,
                recommendedAngle = 90, // Servo no centro
                audioAlert = "Caminho Livre",
                hasObstacle = false
            };

            if (detections == null || detections.Count == 0)
                return decision;

            // Encontra o obstáculo mais próximo (a maior caixa delimitadora na tela)
            DetectionResult mostDangerous = detections[0];
            float maxArea = 0;

            foreach (var det in detections)
            {
                float area = det.box.width * det.box.height;
                if (area > maxArea)
                {
                    maxArea = area;
                    mostDangerous = det;
                }
            }

            // 1. EXTRAÇÃO DE ESCALA (Risco baseado no tamanho do obstáculo)
            float relativeHeight = mostDangerous.box.height / screenHeight;
            decision.riskScore = Mathf.Clamp01(relativeHeight / dangerScaleThreshold);

            // Se o objeto está muito longe (pequeno), ignora
            if (decision.riskScore < 0.4f) return decision;

            decision.hasObstacle = true;

            // 2. EXTRAÇÃO DE BORDAS (Onde o obstáculo está localizado?)
            float centerX = mostDangerous.box.x + (mostDangerous.box.width / 2f);
            float relativeX = centerX / screenWidth; // Normaliza entre 0 e 1

            // 3. TOMADA DE DECISÃO (Fiel ao Diagrama La Rosa)
            if (relativeX < 0.33f) 
            {
                // Obstáculo à Esquerda -> Decisão: Desviar pela Direita
                decision.recommendedAngle = 0; 
                decision.audioAlert = "Vá mais à Direita";
            }
            else if (relativeX > 0.66f) 
            {
                // Obstáculo à Direita -> Decisão: Desviar pela Esquerda
                decision.recommendedAngle = 180; 
                decision.audioAlert = "Vá mais à Esquerda";
            }
            else 
            {
                // Obstáculo Frontal -> Decisão: Desviar pelo Centro
                decision.recommendedAngle = 90; 
                decision.audioAlert = "Perigo à frente!";
            }

            return decision;
        }
    }
}