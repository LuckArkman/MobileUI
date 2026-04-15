using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LuckArkman.XR.Networking;
using LuckArkman.XR.AI;
using LuckArkman.XR.Safety;
using LuckArkman.XR.UI;
using LuckArkman.XR.Optimization;

namespace LuckArkman.XR.Main
{
    public class MainSystemOrchestrator : MonoBehaviour
    {
        public enum SystemState { Idle, Searching, Connecting, Active, Warning }
        
        [Header("Referências de Módulos")]
        public WifiDiscoveryManager discoveryManager;
        public MjpegTextureClient mjpegClient; 
        public ActuatorClient actuatorClient; 
        
        public YoloInferenceManager yoloAI;
        public MidasInferenceManager midasAI; 
        public Decision decisionMatrix;       
        
        public HeatmapManager heatmapManager;
        public HudController hudController;
        public BatteryOptimizer batteryOptimizer;
        
        [Header("Módulo de Saída")]
        public Guia sistemaGuia; 

        private SystemState currentState = SystemState.Idle;
        private float tempoDescansoManobra = 0f;

        // ==========================================
        // VARIÁVEIS DO ESCALONADOR EM LOTES (14 FRAMES)
        // ==========================================
        private int contadorFrames = 0;
        private MidasResult ultimoMidasData = new MidasResult();
        private List<DetectionResult> ultimoYoloData = new List<DetectionResult>();

        private void OnEnable()
        {
            if (mjpegClient != null)
            {
                mjpegClient.OnConnected += OnConnectionEstablished;
                mjpegClient.OnDisconnected += OnConnectionLost;
            }
        }

        private void OnDisable()
        {
            if (mjpegClient != null)
            {
                mjpegClient.OnConnected -= OnConnectionEstablished;
                mjpegClient.OnDisconnected -= OnConnectionLost;
            }
        }

        private void Start()
        {
            SetState(SystemState.Searching);
        }

        private void Update()
        {
            if (mjpegClient != null && mjpegClient.IsConnected && currentState != SystemState.Active)
            {
                OnConnectionEstablished();
            }

            if (currentState == SystemState.Active)
            {
                RunActivePipeline();
            }
        }

        private void RunActivePipeline()
        {
            // Se o sistema mandou o usuário girar, a IA tira férias de 1.5s para ele executar o movimento.
            if (Time.time < tempoDescansoManobra) return;

            if (mjpegClient != null && mjpegClient.streamTexture != null && mjpegClient.streamTexture.width > 32)
            {
                // ==============================================================
                // O ESCALONADOR DE 14 FRAMES (DIVISÃO DE CARGA NA GPU)
                // ==============================================================
                
                // (3 MiDaS) -> (3 YOLO) -> (3 MiDaS) -> (3 YOLO) -> (1 MiDaS) -> (1 YOLO)
                if (contadorFrames < 3 || (contadorFrames >= 6 && contadorFrames < 9) || contadorFrames == 12)
                {
                    // TURNO DO MIDAS: Roda a física e confia na memória semântica do YOLO
                    ultimoMidasData = midasAI.ExecuteInference(mjpegClient.streamTexture);
                }
                else 
                {
                    // TURNO DO YOLO: Roda a semântica e confia na memória física do MiDaS
                    ultimoYoloData = GetDetectionsFromAI();
                }

                // Avança o relógio de frames
                contadorFrames++;
                if (contadorFrames >= 14) contadorFrames = 0;

                // ==============================================================
                // TOMADA DE DECISÃO POR FRAME (pontuação + buffer temporal)
                // ==============================================================
                if (decisionMatrix != null && sistemaGuia != null)
                {
                    decisionMatrix.AvaliarCenario(
                        ultimoYoloData,
                        ultimoMidasData,
                        mjpegClient.streamTexture.width
                    );

                    if (contadorFrames == 0)
                    {
                        // CORREÇÃO AQUI: Removido o 'if' que quebrava o compilador.
                        // Agora ele extrai o pacote diretamente do método.
                        Decision.DecisaoPacote pacoteFinal = decisionMatrix.ObterConsenso(out string placar);

                        string descricaoAmbiente = $"Frente: {ultimoMidasData.dangerScore:F1}/10 | Esq: {ultimoMidasData.leftZoneDanger:F1}/10 | Dir: {ultimoMidasData.rightZoneDanger:F1}/10.";
                        if (ultimoYoloData != null && ultimoYoloData.Count > 0)
                        {
                            descricaoAmbiente += $" Há os seguintes objetos em volta: {ultimoYoloData[0].label}.";
                        }

                        int passosCalculados = Mathf.Max(1, 8 - Mathf.RoundToInt(ultimoMidasData.dangerScore));

                        Debug.Log($"[Main - CONSENSO ATINGIDO] Analisados 14 frames. Placar: {placar}. Comando Final Enviado ao Guia: {pacoteFinal.comando}. Motivo semântico: {pacoteFinal.motivoSemantico}.");

                        if (!sistemaGuia.EstaTocandoAudioDeSistema)
                        {
                            // Envia o comando, passos, descrição e o motivo semântico para o Guia.cs
                            sistemaGuia.ExecutarComando(pacoteFinal.comando, passosCalculados, descricaoAmbiente, pacoteFinal.motivoSemantico);
                        }

                        bool isManobraEvasiva = 
                            pacoteFinal.comando != Guia.EstadoInstrucao.Frente1 && 
                            pacoteFinal.comando != Guia.EstadoInstrucao.Frente2 && 
                            pacoteFinal.comando != Guia.EstadoInstrucao.Frente3 && 
                            pacoteFinal.comando != Guia.EstadoInstrucao.Frente4 && 
                            pacoteFinal.comando != Guia.EstadoInstrucao.Nenhum;

                        if (isManobraEvasiva)
                        {
                            tempoDescansoManobra = Time.time + 1.5f;
                        }

                        decisionMatrix.LimparBuffer();
                    }
                }
            }
            
            ProcessSystemSafety(); 
        }

        private List<DetectionResult> GetDetectionsFromAI()
        {
            if (yoloAI != null && mjpegClient != null && mjpegClient.streamTexture != null)
            {
                List<DetectionResult> deteccoes = yoloAI.ExecuteInference(mjpegClient.streamTexture);
                // Ordena por tamanho (os maiores perigos primeiro)
                if (deteccoes != null) {
                    deteccoes.Sort((a, b) => (b.box.width * b.box.height).CompareTo(a.box.width * a.box.height));
                }
                return deteccoes;
            }
            return new List<DetectionResult>();
        }

        private void ProcessSystemSafety()
        {
            List<HeatmapManager.HeatmapPoint> currentPoints = new List<HeatmapManager.HeatmapPoint>();
            if (heatmapManager != null) heatmapManager.UpdateHeatmap(currentPoints);
        }

        public void SetState(SystemState newState)
        {
            currentState = newState;
        }

        public void OnConnectionEstablished()
        {
            SetState(SystemState.Active);
            
            // NOVO: Desliga apenas visualmente o HUD assim que conecta!
            if (hudController != null)
            {
                hudController.SetVisibility(false);
            }
            
            if (sistemaGuia != null)
            {
                sistemaGuia.IniciarSequenciaDeAudioDeSistema();
            }
        }

        public void OnConnectionLost()
        {
            SetState(SystemState.Searching);
            
            // NOVO: Religa visualmente o HUD se a conexão cair
            if (hudController != null)
            {
                hudController.SetVisibility(true);
            }
            
            if (discoveryManager != null) discoveryManager.StartDiscovery();
        }
    }
}