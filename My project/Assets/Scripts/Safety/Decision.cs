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
        [Header("Configurações de Decisão")]
        public float thresholdAcao = 5.0f;
        public int tamanhoBuffer = 10;

        [Header("Integração Visual")]
        public HeatmapManager heatmapManager;

        private struct DadosFrame
        {
            public float riscoCombinado;
            public float perigoEsq;
            public float perigoDir;
            public float dimensao;
            public float bloqueioGeral;
        }
        private Queue<DadosFrame> historicoFrames = new Queue<DadosFrame>();

        private bool emEscapatoria = false;
        private float bloqueioAntesDoGiro = 0f;
        private bool girouParaEsquerda = false;

        private Dictionary<string, float> yoloRiskWeights = new Dictionary<string, float>()
        {
            { "carro", 5.0f }, { "caminhao", 5.0f }, { "onibus", 5.0f }, { "moto", 5.0f }, { "aviao", 5.0f },
            { "bicicleta", 4.0f }, { "trem", 4.0f },
            { "pessoa", 3.0f }, { "semaforo", 3.0f }, { "hidrante", 3.0f }, { "placa pare", 3.0f },
            { "cadeira", 1.0f }, { "cachorro", 1.0f }, { "gato", 1.0f }, { "mochila", 1.0f },
            { "monitor", 2.0f }, { "laptop", 2.0f }, { "tv", 2.0f }, { "porta", 3.0f }
        };

        // Classificação do que é dinâmico no ambiente
        private string[] objetosMoveis = { "pessoa", "carro", "moto", "bicicleta", "onibus", "caminhao", "cachorro", "gato" };

        public void LimparBuffer()
        {
            historicoFrames.Clear();
        }

        public Guia.EstadoInstrucao AvaliarCenario(List<DetectionResult> yoloDetections, MidasResult midasData, int screenWidth)
        {
            float yoloMaxRisk = 2.5f;
            float dimensaoInstantanea = 1f;
            bool temObjetoMovelPerto = false;

            // PREPARAÇÃO PARA O HEATMAP
            List<HeatmapManager.HeatmapPoint> pontosHeatmap = new List<HeatmapManager.HeatmapPoint>();

            // 1. PROCESSAMENTO DO YOLO (Semântica)
            if (yoloDetections != null && yoloDetections.Count > 0)
            {
                DetectionResult perigoPrincipal = yoloDetections[0];
                string labelPrincipal = perigoPrincipal.label.ToLower();
                if (yoloRiskWeights.TryGetValue(labelPrincipal, out float risk)) yoloMaxRisk = risk;

                float ocupacaoTela = perigoPrincipal.box.width / screenWidth;
                dimensaoInstantanea = Mathf.Clamp(Mathf.Ceil(ocupacaoTela * 5f), 1f, 5f);

                foreach (var det in yoloDetections)
                {
                    string label = det.label.ToLower();
                    float detRisk = 1.0f;
                    yoloRiskWeights.TryGetValue(label, out detRisk);
                    
                    float ocupacaoObj = det.box.width / screenWidth;

                    // Alimenta o array do Heatmap com o YOLO
                    pontosHeatmap.Add(new HeatmapManager.HeatmapPoint {
                        x = det.box.center.x / screenWidth, 
                        y = det.box.center.y / screenWidth, 
                        size = ocupacaoObj,
                        riskScore = detRisk
                    });

                    if (objetosMoveis.Contains(label) && ocupacaoObj > 0.2f)
                    {
                        temObjetoMovelPerto = true;
                    }
                }
            }

            // 2. PROCESSAMENTO DO MIDAS PARA O HEATMAP (Profundidade Física)
            // Se o Midas detectar que as coisas estão perto fisicamente, cria grandes manchas de calor.
            
            if (midasData.leftZoneDanger > 3.0f)
            {
                pontosHeatmap.Add(new HeatmapManager.HeatmapPoint {
                    x = 0.25f, // Fica do lado esquerdo da tela
                    y = 0.5f,
                    size = 0.6f, // Mancha larga para cobrir o lado esquerdo
                    riskScore = midasData.leftZoneDanger * 0.5f // Normalizando para a escala de 1 a 5 do YOLO
                });
            }

            if (midasData.rightZoneDanger > 3.0f)
            {
                pontosHeatmap.Add(new HeatmapManager.HeatmapPoint {
                    x = 0.75f, // Fica do lado direito da tela
                    y = 0.5f,
                    size = 0.6f,
                    riskScore = midasData.rightZoneDanger * 0.5f
                });
            }

            if (midasData.dangerScore > 4.0f)
            {
                pontosHeatmap.Add(new HeatmapManager.HeatmapPoint {
                    x = 0.5f, // Centro absoluto
                    y = 0.5f,
                    size = 0.8f, // Mancha gigante cobrindo o meio
                    riskScore = midasData.dangerScore * 0.6f 
                });
            }

            // ATUALIZA A PLACA DE VÍDEO (GPU) COM A FUSÃO (YOLO + MIDAS)
            if (heatmapManager != null) heatmapManager.UpdateHeatmap(pontosHeatmap);

            // ====================================================================
            // CÁLCULOS MATEMÁTICOS DE RISCO
            // ====================================================================
            float riscoInstantaneo = (midasData.dangerScore * 0.7f) + (yoloMaxRisk * 0.5f);
            float bloqueioGeralInstantaneo = (midasData.leftZoneDanger + midasData.rightZoneDanger + midasData.dangerScore) / 3f;

            if (midasData.dangerScore >= 4.5f)
            {
                if (bloqueioGeralInstantaneo >= 5.5f) dimensaoInstantanea = Mathf.Max(dimensaoInstantanea, 5f);
                else if (bloqueioGeralInstantaneo >= 4.0f) dimensaoInstantanea = Mathf.Max(dimensaoInstantanea, 3f);
            }

            // REFLEXO DE EMERGÊNCIA ABSOLUTA
            if (midasData.absoluteVelocityAlert && riscoInstantaneo > 7.5f)
            {
                LimparBuffer(); 
                emEscapatoria = false; 
                bool esqL = midasData.leftZoneDanger < 6.0f;
                bool dirL = midasData.rightZoneDanger < 6.0f;
                if (!esqL && !dirL) return Guia.EstadoInstrucao.Parar;
                if (esqL && dirL) return midasData.leftZoneDanger < midasData.rightZoneDanger ? Guia.EstadoInstrucao.DesviarEsquerda : Guia.EstadoInstrucao.DesviarDireita;
                else return esqL ? Guia.EstadoInstrucao.DesviarEsquerda : Guia.EstadoInstrucao.DesviarDireita;
            }

            historicoFrames.Enqueue(new DadosFrame {
                riscoCombinado = riscoInstantaneo, perigoEsq = midasData.leftZoneDanger,
                perigoDir = midasData.rightZoneDanger, dimensao = dimensaoInstantanea, bloqueioGeral = bloqueioGeralInstantaneo
            });

            if (historicoFrames.Count > tamanhoBuffer) historicoFrames.Dequeue();

            float avgRisco = historicoFrames.Average(f => f.riscoCombinado);
            float avgEsq = historicoFrames.Average(f => f.perigoEsq);
            float avgDir = historicoFrames.Average(f => f.perigoDir);
            float avgDimensao = historicoFrames.Max(f => f.dimensao); 
            float avgBloqueioGeral = historicoFrames.Average(f => f.bloqueioGeral);

            bool isEspacoAberto = (avgEsq < 4.0f && avgDir < 4.0f);
            bool esqLivre = avgEsq < 6.0f;
            bool dirLivre = avgDir < 6.0f;

            // FUGA DE CANTOS E BECOS
            if (emEscapatoria)
            {
                if (avgRisco < thresholdAcao && (esqLivre || dirLivre))
                {
                    emEscapatoria = false; 
                }
                else
                {
                    if (historicoFrames.Count >= tamanhoBuffer / 2)
                    {
                        if (avgBloqueioGeral < bloqueioAntesDoGiro)
                        {
                            bloqueioAntesDoGiro = avgBloqueioGeral;
                            LimparBuffer();
                            return girouParaEsquerda ? Guia.EstadoInstrucao.GirarEsquerda : Guia.EstadoInstrucao.GirarDireita;
                        }
                        else
                        {
                            emEscapatoria = false;
                            LimparBuffer();
                            return girouParaEsquerda ? Guia.EstadoInstrucao.GirarDireita : Guia.EstadoInstrucao.GirarEsquerda;
                        }
                    }
                    else return Guia.EstadoInstrucao.Nenhum;
                }
            }

            // =========================================================================
            // SOBREPOSIÇÃO DO GPS E PRIORIDADE ROTACIONAL
            // =========================================================================
            if (avgRisco < 4.0f && NavigationManager.Instance != null && NavigationManager.Instance.anguloRelativoAoDestino != 0)
            {
                float anguloRelativo = NavigationManager.Instance.anguloRelativoAoDestino;

                if (Mathf.Abs(anguloRelativo) > 120f)
                {
                    if (temObjetoMovelPerto) return Guia.EstadoInstrucao.Parar;
                    
                    LimparBuffer();
                    return anguloRelativo > 0 ? Guia.EstadoInstrucao.GirarDireita : Guia.EstadoInstrucao.GirarEsquerda;
                }

                if (anguloRelativo > 30f) return Guia.EstadoInstrucao.GirarDireita;
                if (anguloRelativo < -30f) return Guia.EstadoInstrucao.GirarEsquerda;
            }

            // OBSTÁCULO IMINENTE (Perigos Estáticos / Bloqueios)
            if (avgRisco >= thresholdAcao)
            {
                if (!esqLivre && !dirLivre)
                {
                    emEscapatoria = true;
                    bloqueioAntesDoGiro = avgBloqueioGeral;
                    girouParaEsquerda = avgEsq < avgDir;
                    LimparBuffer(); 
                    return girouParaEsquerda ? Guia.EstadoInstrucao.GirarEsquerda : Guia.EstadoInstrucao.GirarDireita;
                }

                bool fugirParaEsquerda = false;
                if (esqLivre && dirLivre) fugirParaEsquerda = avgEsq < avgDir;
                else if (esqLivre) fugirParaEsquerda = true;
                else fugirParaEsquerda = false;

                Guia.EstadoInstrucao acaoEscolhida;

                if (!isEspacoAberto)
                {
                    if (avgDimensao < 2.0f) acaoEscolhida = fugirParaEsquerda ? Guia.EstadoInstrucao.DesviarEsquerda : Guia.EstadoInstrucao.DesviarDireita;
                    else acaoEscolhida = fugirParaEsquerda ? Guia.EstadoInstrucao.GirarEsquerda : Guia.EstadoInstrucao.GirarDireita;
                }
                else
                {
                    if (avgDimensao >= 2.5f) acaoEscolhida = fugirParaEsquerda ? Guia.EstadoInstrucao.GirarEsquerda : Guia.EstadoInstrucao.GirarDireita;
                    else acaoEscolhida = fugirParaEsquerda ? Guia.EstadoInstrucao.DesviarEsquerda : Guia.EstadoInstrucao.DesviarDireita;
                }

                LimparBuffer();
                return acaoEscolhida;
            }

            // PROGRESSÃO FRONTAL 
            if (avgRisco < 2.0f) return Guia.EstadoInstrucao.Frente4;
            else if (avgRisco < 3.2f) return Guia.EstadoInstrucao.Frente3;
            else if (avgRisco < 4.2f) return Guia.EstadoInstrucao.Frente2;
            else return Guia.EstadoInstrucao.Frente1;
        }
    }
}