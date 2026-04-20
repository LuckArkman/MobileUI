using UnityEngine;
using System.Collections.Generic;
using LuckArkman.XR.AI;
using LuckArkman.XR.Main;
using LuckArkman.XR.Navigation;
using System.Linq;

namespace LuckArkman.XR.Safety
{
    public class Decision : MonoBehaviour
    {
        public struct DecisaoPacote
        {
            public Guia.EstadoInstrucao comando;
            public string motivoSemantico;
        }

        [Header("Configurações de Decisão")]
        public int consensoFrameCount = 14;
        public float intentionBonus = 50f;
        public float safetyBonus = 30f;
        public float turnBonus = 90f;
        public float narrowObstacleBonus = 45f;
        public float sideBlockPenalty = 100f;
        public float partialSidePenalty = 70f;
        public float frontPenalty = 35f;
        public float extremeFrontPenalty = 1000f;
        public float objectRoutePenalty = 20f;
        public float objectRouteSafetyBonus = 15f;
        public float rightBlockThreshold = 6.0f;
        public float leftBlockThreshold = 6.0f;
        public float largeTurnThreshold = 45f;
        public float smallTurnThreshold = 15f;
        public float straightThreshold = 15f;

        [Header("Calibração de Câmara")]
        [Tooltip(
            "Inverte os comandos de Esquerda/Direita em todo o pipeline de decisão.\n" +
            "Ative quando a câmara do dispositivo estiver espelhada horizontalmente\n" +
            "e os comandos de desvio estiverem trocados."
        )]
        public bool inverterEixoXCamera = false;

        [Header("Rastreamento de Rota")]
        [Tooltip("Arraste o RouteProgressTracker que contém os waypoints da rota aqui.")]
        public LuckArkman.XR.Navigation.RouteProgressTracker routeTracker;

        [Tooltip("Transform do utilizador/dispositivo usado para calcular a direção ao próximo waypoint.")]
        public Transform userTransform;

        [Header("Integração Visual")]
        public HeatmapManager heatmapManager;

        private Queue<DecisaoPacote> frameWinners = new Queue<DecisaoPacote>();

        private Dictionary<string, float> yoloRiskWeights = new Dictionary<string, float>()
        {
            { "carro", 5.0f }, { "caminhao", 5.0f }, { "onibus", 5.0f }, { "moto", 5.0f }, { "aviao", 5.0f },
            { "bicicleta", 4.0f }, { "trem", 4.0f },
            { "pessoa", 3.0f }, { "semaforo", 3.0f }, { "hidrante", 3.0f }, { "placa pare", 3.0f },
            { "cadeira", 1.0f }, { "cachorro", 1.0f }, { "gato", 1.0f }, { "mochila", 1.0f },
            { "monitor", 2.0f }, { "laptop", 2.0f }, { "tv", 2.0f }, { "porta", 3.0f }
        };

        private string[] objetosMoveis = { "pessoa", "carro", "moto", "bicicleta", "onibus", "caminhao", "cachorro", "gato" };

        public void LimparBuffer()
        {
            frameWinners.Clear();
        }

        public DecisaoPacote AvaliarCenario(List<DetectionResult> yoloDetections, MidasResult midasData, int screenWidth)
        {
            // ── Calcula ângulo de navegação ao próximo waypoint ────────────────────────
            float anguloRelativo = 0f;

            if (routeTracker != null && !routeTracker.IsRouteComplete &&
                routeTracker.CurrentWaypoint != null && userTransform != null)
            {
                // Direção ao próximo waypoint no plano horizontal
                Vector3 dirWaypoint = routeTracker.CurrentWaypoint.position - userTransform.position;
                dirWaypoint.y = 0f;

                // Ângulo assinado: positivo = waypoint à direita, negativo = à esquerda
                anguloRelativo = Vector3.SignedAngle(
                    userTransform.forward,
                    dirWaypoint.normalized,
                    Vector3.up
                );

                // Aplica inversão de eixo se câmara estiver espelhada
                if (inverterEixoXCamera) anguloRelativo = -anguloRelativo;

                Debug.Log($"[Decision] Waypoint {routeTracker.currentWaypointIndex + 1}: " +
                          $"ângulo={anguloRelativo:F1}° | dist={routeTracker.GetDistanceToCurrentWaypoint():F1}m");
            }
            else if (NavigationManager.Instance != null && NavigationManager.Instance.isNavigating)
            {
                anguloRelativo = NavigationManager.Instance.anguloRelativoAoDestino;
                if (inverterEixoXCamera) anguloRelativo = -anguloRelativo;
            }

            string motivoSemantico = string.Empty;
            bool temObjetoCriticoNaRota = false;
            string principalLabel = string.Empty;
            float principalWidthRatio = 0f;
            float principalCenter = 0.5f;
            float yoloMaxRisk = 2.5f;
            List<HeatmapManager.HeatmapPoint> heatmapPoints = new List<HeatmapManager.HeatmapPoint>();

            if (yoloDetections != null && yoloDetections.Count > 0)
            {
                var principalDet = yoloDetections
                    .OrderByDescending(d => d.box.width * d.box.height)
                    .First();

                principalLabel = principalDet.label.ToString().ToLower();
                principalWidthRatio = principalDet.box.width / screenWidth;
                principalCenter = principalDet.box.center.x / screenWidth;

                foreach (var det in yoloDetections)
                {
                    string label = det.label.ToString().ToLower();
                    float risk = 1.0f;
                    yoloRiskWeights.TryGetValue(label, out risk);

                    if (risk > yoloMaxRisk) yoloMaxRisk = risk;

                    float ocupacaoObj = det.box.width / screenWidth;
                    float xCenter = det.box.center.x / screenWidth;

                    heatmapPoints.Add(new HeatmapManager.HeatmapPoint
                    {
                        x = xCenter,
                        y = det.box.center.y / screenWidth,
                        size = ocupacaoObj,
                        riskScore = risk
                    });

                    bool objetoMovel = objetosMoveis.Contains(label);
                    bool estaNoCentro = xCenter > 0.25f && xCenter < 0.75f;

                    if (objetoMovel && estaNoCentro && ocupacaoObj > 0.12f)
                    {
                        temObjetoCriticoNaRota = true;
                    }
                }
            }

            if (midasData.leftZoneDanger > 3.0f)
            {
                heatmapPoints.Add(new HeatmapManager.HeatmapPoint
                {
                    x = 0.25f,
                    y = 0.5f,
                    size = 0.6f,
                    riskScore = midasData.leftZoneDanger * 0.5f
                });
            }

            if (midasData.rightZoneDanger > 3.0f)
            {
                heatmapPoints.Add(new HeatmapManager.HeatmapPoint
                {
                    x = 0.75f,
                    y = 0.5f,
                    size = 0.6f,
                    riskScore = midasData.rightZoneDanger * 0.5f
                });
            }

            if (midasData.dangerScore > 4.0f)
            {
                heatmapPoints.Add(new HeatmapManager.HeatmapPoint
                {
                    x = 0.5f,
                    y = 0.5f,
                    size = 0.8f,
                    riskScore = midasData.dangerScore * 0.6f
                });
            }

            if (heatmapManager != null)
            {
                heatmapManager.UpdateHeatmap(heatmapPoints);
            }

            var scores = InicializarPontuacoes();
            var intencao = Guia.EstadoInstrucao.Nenhum;
            if (NavigationManager.Instance != null && NavigationManager.Instance.isNavigating)
            {
                intencao = AplicarIntencaoGPS(scores, anguloRelativo, midasData.dangerScore);
            }

            if (midasData.dangerScore > 6.0f)
            {
                scores[Guia.EstadoInstrucao.Frente1] -= extremeFrontPenalty;
                scores[Guia.EstadoInstrucao.Frente2] -= extremeFrontPenalty;
                scores[Guia.EstadoInstrucao.Frente3] -= extremeFrontPenalty;
                scores[Guia.EstadoInstrucao.Frente4] -= extremeFrontPenalty;
            }

            if (principalWidthRatio > 0f && principalWidthRatio < 0.3f)
            {
                // Aplica inversão de eixo X se necessário
                float centerCorrigido = inverterEixoXCamera ? (1f - principalCenter) : principalCenter;
                bool rightObstacle = centerCorrigido >= 0.5f;

                if (rightObstacle && midasData.leftZoneDanger < leftBlockThreshold)
                {
                    scores[Guia.EstadoInstrucao.DesviarEsquerda] += narrowObstacleBonus;
                    Debug.Log($"[Decision] Objeto estreito à DIREITA ({principalLabel} {principalWidthRatio:P0}) → Desviar para Esquerda");
                }
                else if (!rightObstacle && midasData.rightZoneDanger < rightBlockThreshold)
                {
                    scores[Guia.EstadoInstrucao.DesviarDireita] += narrowObstacleBonus;
                    Debug.Log($"[Decision] Objeto estreito à ESQUERDA ({principalLabel} {principalWidthRatio:P0}) → Desviar para Direita");
                }
            }

            if (principalWidthRatio > 0.5f || Mathf.Abs(anguloRelativo) > largeTurnThreshold)
            {
                // Para objecto largo: o utilizador deve girar para o lado oposto ao objecto
                // Para GPS: positivo = waypoint à direita = girar direita
                float centerCorrigido2 = inverterEixoXCamera ? (1f - principalCenter) : principalCenter;
                bool preferRight = principalWidthRatio > 0.5f ? centerCorrigido2 < 0.5f : anguloRelativo > 0f;

                if (preferRight)
                {
                    scores[Guia.EstadoInstrucao.GirarDireita] += turnBonus;
                    Debug.Log($"[Decision] Giro DIREITA: lab={principalLabel} largura={principalWidthRatio:P0} ângulo={anguloRelativo:F0}°");
                }
                else
                {
                    scores[Guia.EstadoInstrucao.GirarEsquerda] += turnBonus;
                    Debug.Log($"[Decision] Giro ESQUERDA: lab={principalLabel} largura={principalWidthRatio:P0} ângulo={anguloRelativo:F0}°");
                }
            }

            bool temPoliticaDeSeguranca = false;
            string overrideReason = string.Empty;
            AplicarPenalidadesDeSeguranca(
                scores, midasData, temObjetoCriticoNaRota, ref overrideReason, intencao, principalLabel, ref temPoliticaDeSeguranca);

            if (temObjetoCriticoNaRota && string.IsNullOrEmpty(motivoSemantico))
            {
                motivoSemantico = principalLabel;
            }

            var vencedor = scores.OrderByDescending(kv => kv.Value)
                                .ThenBy(kv => (int)kv.Key)
                                .Select(kv => kv.Key)
                                .FirstOrDefault();

            if (vencedor == Guia.EstadoInstrucao.Nenhum)
            {
                vencedor = EscolherFrentePorPerigo(midasData.dangerScore);
            }

            if (vencedor != intencao && !string.IsNullOrEmpty(overrideReason))
            {
                if (string.IsNullOrEmpty(motivoSemantico))
                    motivoSemantico = principalLabel;
                Debug.Log($"[Decision - Override] Intenção era {FormatarRotulo(intencao)} (Ângulo {anguloRelativo:F0}°), mas {overrideReason}. Pontuação recalculada. Vencedora do Frame: {FormatarRotulo(vencedor)}.");
            }

            var pacote = new DecisaoPacote
            {
                comando = vencedor,
                motivoSemantico = motivoSemantico
            };

            RegistrarVitoria(pacote);
            return pacote;
        }

        private Dictionary<Guia.EstadoInstrucao, float> InicializarPontuacoes()
        {
            return new Dictionary<Guia.EstadoInstrucao, float>
            {
                { Guia.EstadoInstrucao.Nenhum, 0f },
                { Guia.EstadoInstrucao.Parar, 0f },
                { Guia.EstadoInstrucao.DesviarDireita, 0f },
                { Guia.EstadoInstrucao.DesviarEsquerda, 0f },
                { Guia.EstadoInstrucao.GirarDireita, 0f },
                { Guia.EstadoInstrucao.GirarEsquerda, 0f },
                { Guia.EstadoInstrucao.Frente1, 0f },
                { Guia.EstadoInstrucao.Frente2, 0f },
                { Guia.EstadoInstrucao.Frente3, 0f },
                { Guia.EstadoInstrucao.Frente4, 0f },
            };
        }

        private Guia.EstadoInstrucao AplicarIntencaoGPS(Dictionary<Guia.EstadoInstrucao, float> scores, float anguloRelativo, float dangerScore)
        {
            if (Mathf.Approximately(anguloRelativo, 0f) || Mathf.Abs(anguloRelativo) <= straightThreshold)
            {
                var frente = EscolherFrentePorPerigo(dangerScore);
                scores[frente] += intentionBonus;
                return frente;
            }

            if (anguloRelativo > largeTurnThreshold)
            {
                scores[Guia.EstadoInstrucao.GirarDireita] += intentionBonus;
                return Guia.EstadoInstrucao.GirarDireita;
            }

            if (anguloRelativo < -largeTurnThreshold)
            {
                scores[Guia.EstadoInstrucao.GirarEsquerda] += intentionBonus;
                return Guia.EstadoInstrucao.GirarEsquerda;
            }

            if (anguloRelativo > smallTurnThreshold)
            {
                scores[Guia.EstadoInstrucao.DesviarDireita] += intentionBonus;
                return Guia.EstadoInstrucao.DesviarDireita;
            }

            if (anguloRelativo < -smallTurnThreshold)
            {
                scores[Guia.EstadoInstrucao.DesviarEsquerda] += intentionBonus;
                return Guia.EstadoInstrucao.DesviarEsquerda;
            }

            var fallbackFrente = EscolherFrentePorPerigo(dangerScore);
            scores[fallbackFrente] += intentionBonus;
            return fallbackFrente;
        }

        private void AplicarPenalidadesDeSeguranca(Dictionary<Guia.EstadoInstrucao, float> scores, MidasResult midasData, bool temObjetoCriticoNaRota, ref string overrideReason, Guia.EstadoInstrucao intencao, string principalLabel, ref bool temPoliticaDeSeguranca)
        {
            bool rightBlocked = midasData.rightZoneDanger > rightBlockThreshold;
            bool leftBlocked = midasData.leftZoneDanger > leftBlockThreshold;

            if (rightBlocked)
            {
                scores[Guia.EstadoInstrucao.GirarDireita] -= sideBlockPenalty;
                scores[Guia.EstadoInstrucao.DesviarDireita] -= partialSidePenalty;
                scores[Guia.EstadoInstrucao.DesviarEsquerda] += safetyBonus;
                if (intencao == Guia.EstadoInstrucao.GirarDireita || intencao == Guia.EstadoInstrucao.DesviarDireita)
                {
                    overrideReason = $"Dir está bloqueada (MiDaS: {midasData.rightZoneDanger:F1})";
                    temPoliticaDeSeguranca = true;
                }
            }

            if (leftBlocked)
            {
                scores[Guia.EstadoInstrucao.GirarEsquerda] -= sideBlockPenalty;
                scores[Guia.EstadoInstrucao.DesviarEsquerda] -= partialSidePenalty;
                scores[Guia.EstadoInstrucao.DesviarDireita] += safetyBonus;
                if (intencao == Guia.EstadoInstrucao.GirarEsquerda || intencao == Guia.EstadoInstrucao.DesviarEsquerda)
                {
                    overrideReason = $"Esq está bloqueada (MiDaS: {midasData.leftZoneDanger:F1})";
                    temPoliticaDeSeguranca = true;
                }
            }

            if (midasData.dangerScore > 7.0f) 
            {
                scores[Guia.EstadoInstrucao.Frente4] -= frontPenalty;
                scores[Guia.EstadoInstrucao.Frente3] -= frontPenalty * 0.8f;
                scores[Guia.EstadoInstrucao.Frente2] -= frontPenalty * 0.5f;
                scores[Guia.EstadoInstrucao.Frente1] -= frontPenalty * 0.2f;
    
                if (intencao.ToString().StartsWith("Frente"))
                {
                    overrideReason = $"Frente bloqueada (MiDaS: {midasData.dangerScore:F1})";
                    temPoliticaDeSeguranca = true;
                }
            }

            if (temObjetoCriticoNaRota)
            {
                scores[Guia.EstadoInstrucao.Frente4] -= objectRoutePenalty;
                scores[Guia.EstadoInstrucao.Frente3] -= objectRoutePenalty * 0.7f;
                scores[Guia.EstadoInstrucao.Frente2] -= objectRoutePenalty * 0.5f;
                scores[Guia.EstadoInstrucao.Frente1] -= objectRoutePenalty * 0.2f;
                scores[Guia.EstadoInstrucao.DesviarEsquerda] += objectRouteSafetyBonus;
                scores[Guia.EstadoInstrucao.DesviarDireita] += objectRouteSafetyBonus;
                if (intencao.ToString().StartsWith("Frente") && string.IsNullOrEmpty(overrideReason))
                {
                    overrideReason = $"obstáculo crítico na rota ({principalLabel})";
                    temPoliticaDeSeguranca = true;
                }
            }
        }

        private Guia.EstadoInstrucao EscolherFrentePorPerigo(float dangerScore)
        {
            if (dangerScore < 3.0f) return Guia.EstadoInstrucao.Frente4; // Caminho totalmente livre
            if (dangerScore < 5.5f) return Guia.EstadoInstrucao.Frente3; // Espaço bom (> 1.7m)
            if (dangerScore < 7.0f) return Guia.EstadoInstrucao.Frente2; // Cuidado, mas dá pra avançar (ex: 1.2m)
            return Guia.EstadoInstrucao.Frente1; // Muito perto (< 1m), ir passo a passo cauteloso
        }

        private void RegistrarVitoria(DecisaoPacote pacote)
        {
            frameWinners.Enqueue(pacote);
            while (frameWinners.Count > consensoFrameCount)
            {
                frameWinners.Dequeue();
            }
        }

        public DecisaoPacote ObterConsenso(out string placar)
        {
            placar = string.Empty;
            if (frameWinners.Count == 0)
            {
                return new DecisaoPacote { comando = Guia.EstadoInstrucao.Nenhum, motivoSemantico = string.Empty };
            }

            var contagem = frameWinners
                .GroupBy(x => x.comando)
                .ToDictionary(g => g.Key, g => g.Count());

            var comandoFinal = contagem
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => (int)kv.Key)
                .Select(kv => kv.Key)
                .First();

            placar = string.Join(", ", contagem
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{FormatarRotulo(kv.Key)} ({kv.Value} vitórias)"));

            string motivoSemantico = frameWinners
                .Reverse()
                .FirstOrDefault(x => x.comando == comandoFinal && !string.IsNullOrEmpty(x.motivoSemantico))
                .motivoSemantico;

            return new DecisaoPacote
            {
                comando = comandoFinal,
                motivoSemantico = motivoSemantico
            };
        }

        public Guia.EstadoInstrucao ObterConsenso(out string placar, out string motivoSemantico)
        {
            DecisaoPacote pacote = ObterConsenso(out placar);
            motivoSemantico = pacote.motivoSemantico;
            return pacote.comando;
        }

        private string FormatarRotulo(Guia.EstadoInstrucao comando)
        {
            switch (comando)
            {
                case Guia.EstadoInstrucao.DesviarEsquerda: return "DesviarEsq";
                case Guia.EstadoInstrucao.DesviarDireita: return "DesviarDir";
                case Guia.EstadoInstrucao.GirarEsquerda: return "GirarEsq";
                case Guia.EstadoInstrucao.GirarDireita: return "GirarDir";
                case Guia.EstadoInstrucao.Frente1: return "Frente1";
                case Guia.EstadoInstrucao.Frente2: return "Frente2";
                case Guia.EstadoInstrucao.Frente3: return "Frente3";
                case Guia.EstadoInstrucao.Frente4: return "Frente4";
                default: return comando.ToString();
            }
        }
    }
}