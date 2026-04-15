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

            List<HeatmapManager.HeatmapPoint> pontosHeatmap = new List<HeatmapManager.HeatmapPoint>();

            if (yoloDetections != null && yoloDetections.Count > 0)
            {
                DetectionResult perigoPrincipal = yoloDetections[0];
                string labelPrincipal = perigoPrincipal.label.ToString().ToLower();
                if (yoloRiskWeights.TryGetValue(labelPrincipal, out float risk)) yoloMaxRisk = risk;

                float ocupacaoTela = perigoPrincipal.box.width / screenWidth;
                dimensaoInstantanea = Mathf.Clamp(Mathf.Ceil(ocupacaoTela * 5f), 1f, 5f);

                foreach (var det in yoloDetections)
                {
                    string label = det.label.ToString().ToLower();
                    float detRisk = 1.0f;
                    yoloRiskWeights.TryGetValue(label, out detRisk);
                    
                    float ocupacaoObj = det.box.width / screenWidth;

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

            if (midasData.leftZoneDanger > 3.0f)
            {
                pontosHeatmap.Add(new HeatmapManager.HeatmapPoint {
                    x = 0.25f, y = 0.5f, size = 0.6f, riskScore = midasData.leftZoneDanger * 0.5f 
                });
            }

            if (midasData.rightZoneDanger > 3.0f)
            {
                pontosHeatmap.Add(new HeatmapManager.HeatmapPoint {
                    x = 0.75f, y = 0.5f, size = 0.6f, riskScore = midasData.rightZoneDanger * 0.5f
                });
            }

            if (midasData.dangerScore > 4.0f)
            {
                pontosHeatmap.Add(new HeatmapManager.HeatmapPoint {
                    x = 0.5f, y = 0.5f, size = 0.8f, riskScore = midasData.dangerScore * 0.6f 
                });
            }

            if (heatmapManager != null) heatmapManager.UpdateHeatmap(pontosHeatmap);

            float riscoInstantaneo = (midasData.dangerScore * 0.7f) + (yoloMaxRisk * 0.5f);
            float bloqueioGeralInstantaneo = (midasData.leftZoneDanger + midasData.rightZoneDanger + midasData.dangerScore) / 3f;

            if (midasData.dangerScore >= 4.5f)
            {
                if (bloqueioGeralInstantaneo >= 5.5f) dimensaoInstantanea = Mathf.Max(dimensaoInstantanea, 5f);
                else if (bloqueioGeralInstantaneo >= 4.0f) dimensaoInstantanea = Mathf.Max(dimensaoInstantanea, 3f);
            }

            // OTIMIZAÇÃO: Reflexo de emergência mais rigoroso
            if (midasData.absoluteVelocityAlert && riscoInstantaneo > 7.0f)
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
            // SOBREPOSIÇÃO DO GPS E PRIORIDADE ROTACIONAL (OTIMIZADA)
            // =========================================================================
            if (avgRisco < 4.0f && NavigationManager.Instance != null && NavigationManager.Instance.isNavigating)
            {
                float anguloRelativo = NavigationManager.Instance.anguloRelativoAoDestino;

                if (anguloRelativo != 0f)
                {
                    // O GPS exige uma manobra grande (curva ou retorno)
                    if (Mathf.Abs(anguloRelativo) > 90f)
                    {
                        if (temObjetoMovelPerto) return Guia.EstadoInstrucao.Parar;
                        
                        // IA verifica se a curva é segura fisicamente
                        if (anguloRelativo > 0 && avgDir < 4.0f) 
                        {
                            LimparBuffer();
                            return Guia.EstadoInstrucao.GirarDireita;
                        }
                        if (anguloRelativo < 0 && avgEsq < 4.0f) 
                        {
                            LimparBuffer();
                            return Guia.EstadoInstrucao.GirarEsquerda;
                        }
                        
                        return Guia.EstadoInstrucao.Parar; // Espera o lado liberar
                    }

                    // O GPS pede apenas uma correção de rota leve (ex: contornar uma pequena praça)
                    if (anguloRelativo > 0f && avgDir < 4.5f) 
                    {
                        return Guia.EstadoInstrucao.DesviarDireita; 
                    }
                    if (anguloRelativo < 0f && avgEsq < 4.5f) 
                    {
                        return Guia.EstadoInstrucao.DesviarEsquerda; 
                    }
                }
            }

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

            if (avgRisco < 2.0f) return Guia.EstadoInstrucao.Frente4;
            else if (avgRisco < 3.2f) return Guia.EstadoInstrucao.Frente3;
            else if (avgRisco < 4.2f) return Guia.EstadoInstrucao.Frente2;
            else return Guia.EstadoInstrucao.Frente1;
        }
    }
}