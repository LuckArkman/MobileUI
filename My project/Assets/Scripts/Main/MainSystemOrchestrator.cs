using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LuckArkman.XR.Networking;
using LuckArkman.XR.AI;
using LuckArkman.XR.Safety;
using LuckArkman.XR.Navigation;
using LuckArkman.XR.UI;
using LuckArkman.XR.Optimization;

namespace LuckArkman.XR.Main
{
    public class MainSystemOrchestrator : MonoBehaviour
    {
        public enum SystemState
        {
            Idle,
            Searching,
            Connecting,
            Active,
            Warning
        }

        [Header("Referências de Módulos")] public WifiDiscoveryManager discoveryManager;
        public MjpegTextureClient mjpegClient;
        public ActuatorClient actuatorClient;

        public YoloInferenceManager yoloAI;

        [Tooltip("Motor de profundidade legado (MiDaS, 256×256). Usado quando useDepthAnythingV2 = false.")]
        public MidasInferenceManager midasAI;

        [Tooltip(
            "Motor de profundidade de produção (Depth Anything V2, 518×518). Priorizado quando useDepthAnythingV2 = true.")]
        public DepthAIManager depthAI;

        [Tooltip(
            "Se verdadeiro, usa o Depth Anything V2 (modelo de produção, maior precisão).\n" +
            "Se falso, usa o MiDaS legado (mais rápido, menor precisão).\n" +
            "Recomendado: true em dispositivos com NPU (Snapdragon 8+ / A15+)."
        )]
        public bool useDepthAnythingV2 = true;

        public Decision decisionMatrix;

        public HeatmapManager heatmapManager;
        public HudController hudController;
        public BatteryOptimizer batteryOptimizer;

        [Header("Módulo de Saída")] public Guia sistemaGuia;

        [Tooltip("Gerenciador de progresso do roteiro e evasão de obstáculos.")]
        public RouteProgressManager routeProgress;

        [Tooltip("Bengala Virtual: converte o Depth Map em distâncias métricas reais (metros/passos).")]
        public RaycastScanner raycastScanner;

        [Tooltip("Pediômetro IMU: detecta passos e informa se o usuário efetivamente obedeceu ao comando.")]
        public OdometryTracker odometryTracker;

        private SystemState currentState = SystemState.Idle;

        // Timer de descanso entre manobras evasivas.
        // Com OdometryTracker conectado, este timer é liberado cedo ao detectar passos.
        // Sem OdometryTracker, age como timeout de segurança fixo.
        private float tempoDescansoManobra = 0f;
        private const float TIMEOUT_MANOBRA_FIXO = 4.0f; // Fallback máximo sem o pediômetro

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

            // Escuta os passos do usuário: cada passo verifica se a trava de manobra pode ser liberada
            if (odometryTracker != null)
                odometryTracker.OnPassoDetectado += OnPassoUsuario;
        }

        private void OnDisable()
        {
            if (mjpegClient != null)
            {
                mjpegClient.OnConnected -= OnConnectionEstablished;
                mjpegClient.OnDisconnected -= OnConnectionLost;
            }

            if (odometryTracker != null)
                odometryTracker.OnPassoDetectado -= OnPassoUsuario;
        }

        /// <summary>
        /// Disparado pelo OdometryTracker a cada passo detectado.
        /// Se o usuário já andou o suficiente (passosParaDesbloquear), libera
        /// o timer de manobra antes do timeout de segurança.
        /// </summary>
        private void OnPassoUsuario()
        {
            if (odometryTracker != null && odometryTracker.UsuarioObedeceuComando())
            {
                tempoDescansoManobra = 0f; // libera imediatamente
                Debug.Log("[Orchestrator] 👣 OdometryTracker liberou trava de manobra — usuário obedeceu.");
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
                    // TURNO DE PROFUNDIDADE: Roda o motor selecionado no Inspector.
                    // Depth Anything V2 tem prioridade se disponível e habilitado.
                    // Fallback automático para MiDaS se depthAI não estiver configurado.
                    if (useDepthAnythingV2 && depthAI != null)
                    {
                        ultimoMidasData = depthAI.ExecuteInference(mjpegClient.streamTexture);
                    }
                    else if (midasAI != null)
                    {
                        ultimoMidasData = midasAI.ExecuteInference(mjpegClient.streamTexture);
                    }

                    // BENGALA VIRTUAL: Converte o depth em distâncias métricas reais.
                    if (raycastScanner != null)
                        raycastScanner.Scan(ultimoMidasData);
                }
                else
                {
                    // TURNO DO YOLO: Roda a semântica e confia na memória física do MiDaS
                    ultimoYoloData = GetDetectionsFromAI();
                }

                // Avança o relógio de frames
                contadorFrames++;
                if (contadorFrames >= 14) contadorFrames = 0;

                // -- Alimenta o RouteProgressManager com os dados MiDaS mais recentes
                // (monitoramento contínuo de obstáculos, independente do turno MiDaS/YOLO)
                if (routeProgress != null)
                    routeProgress.AtualizarDadosMidas(ultimoMidasData);

                // FAST-PATH DE SEGURANÇA: Perigo imediato (objeto a < 0.8m ou velocityAlert)
                // É acionado ANTES do consenso de 14 frames para reação instantânea.
                if (raycastScanner != null && raycastScanner.IsImmediateDanger &&
                    sistemaGuia != null && !sistemaGuia.EstaTocandoAudioDeSistema)
                {
                    Debug.LogWarning($"[Orchestrator] 🚨 PERIGO IMEDIATO detectado pelo RaycastScanner " +
                                     $"({raycastScanner.FrontDistanceMeters:F2}m). Forçando PARAR.");
                    sistemaGuia.ExecutarComando(Guia.EstadoInstrucao.Parar, 1, "Obstáculo imediato", "");
                    tempoDescansoManobra = Time.time + 2.0f;
                    return;
                }

                // ==============================================================
                // TOMADA DE DECISÃO POR FRAME (pontuação + buffer temporal)
                // ==============================================================
                if (decisionMatrix != null && sistemaGuia != null)
                {
                    // Se o RaycastScanner estiver disponível, usa dados calibrados (metros reais).
                    // Caso contrário, usa o MidasResult bruto como antes (compatibilidade garantida).
                    MidasResult midasParaDecisao = (raycastScanner != null)
                        ? raycastScanner.ObterMidasCalibrado(ultimoMidasData)
                        : ultimoMidasData;

                    decisionMatrix.AvaliarCenario(
                        ultimoYoloData,
                        midasParaDecisao,
                        mjpegClient.streamTexture.width
                    );

                    if (contadorFrames == 0)
                    {
                        Decision.DecisaoPacote pacoteFinal = decisionMatrix.ObterConsenso(out string placar);

                        // Descrição enriquecida: inclui distâncias métricas reais quando disponíveis
                        string descricaoAmbiente;
                        if (raycastScanner != null)
                        {
                            descricaoAmbiente =
                                $"Frente: {raycastScanner.FrontDistanceMeters:F1}m ({raycastScanner.FrontDistanceSteps}p) | " +
                                $"Esq: {raycastScanner.LeftDistanceMeters:F1}m | Dir: {raycastScanner.RightDistanceMeters:F1}m.";
                        }
                        else
                        {
                            descricaoAmbiente =
                                $"Frente: {ultimoMidasData.dangerScore:F1}/10 | Esq: {ultimoMidasData.leftZoneDanger:F1}/10 | Dir: {ultimoMidasData.rightZoneDanger:F1}/10.";
                        }

                        if (ultimoYoloData != null && ultimoYoloData.Count > 0)
                        {
                            descricaoAmbiente += $" Há os seguintes objetos em volta: {ultimoYoloData[0].label}.";
                        }

                        // Passos calculados: usa o scanner (métrico) se disponível, fallback no score bruto
                        int passosCalculados = (raycastScanner != null)
                            ? raycastScanner.FrontDistanceSteps
                            : Mathf.Max(1, 8 - Mathf.RoundToInt(ultimoMidasData.dangerScore));

                        Debug.Log(
                            $"[Main - CONSENSO ATINGIDO] Analisados 14 frames. Placar: {placar}. Comando Final Enviado ao Guia: {pacoteFinal.comando}. Motivo semântico: {pacoteFinal.motivoSemantico}.");

                        bool isFrente = pacoteFinal.comando.ToString().StartsWith("Frente") ||
                                        pacoteFinal.comando == Guia.EstadoInstrucao.Nenhum;

// Um Giro no próprio eixo para alinhar com o Checkpoint. Não exige passos.
                        bool isGiro = pacoteFinal.comando == Guia.EstadoInstrucao.GirarDireita ||
                                      pacoteFinal.comando == Guia.EstadoInstrucao.GirarEsquerda;

// Um Desvio lateral por causa de um obstáculo.
                        bool isDesvio = pacoteFinal.comando == Guia.EstadoInstrucao.DesviarDireita ||
                                        pacoteFinal.comando == Guia.EstadoInstrucao.DesviarEsquerda ||
                                        pacoteFinal.comando == Guia.EstadoInstrucao.DesviarDuploDireita ||
                                        pacoteFinal.comando == Guia.EstadoInstrucao.DesviarDuploEsquerda;

                        if (!isFrente)
                        {
                            // Se for GIRO (para alinhar com a rota), NUNCA suprime. O utilizador precisa saber para onde virar estando parado.
                            // Se for DESVIO evasivo, suprime apenas se ele não tiver caminhado (evita spam de áudio).
                            bool usuarioMoveu = odometryTracker == null || odometryTracker.UsuarioObedeceuComando();
                            bool deveEmitirComando = isGiro || usuarioMoveu;

                            if (deveEmitirComando && !sistemaGuia.EstaTocandoAudioDeSistema)
                            {
                                sistemaGuia.ExecutarComando(pacoteFinal.comando, passosCalculados, descricaoAmbiente,
                                    pacoteFinal.motivoSemantico);

                                // Notifica apenas se for um desvio que exigia passos.
                                if (isDesvio) odometryTracker?.NotificarComandoEmitido();

                                tempoDescansoManobra = Time.time + TIMEOUT_MANOBRA_FIXO;
                            }
                            else if (!deveEmitirComando)
                            {
                                // DEADLOCK RESOLVIDO: Em vez de ficar em silêncio, se o utilizador está bloqueado a aguardar que o sistema o deixe avançar, 
                                // emitimos um "Frente1" (passo cauteloso) para forçá-lo a sair da inércia com segurança.
                                Debug.Log(
                                    $"[Orchestrator] 🧍 Comando {pacoteFinal.comando} suprimido (sem passos). Injetando Frente1 para quebra de inércia.");
                                sistemaGuia.ExecutarComando(Guia.EstadoInstrucao.Frente1, 1, "Avance um pequeno passo",
                                    "");
                                tempoDescansoManobra = Time.time + 2.0f; // Pausa menor
                            }
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
                if (deteccoes != null)
                {
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