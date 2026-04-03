using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LuckArkman.XR.Networking;
using LuckArkman.XR.AI;
using LuckArkman.XR.Safety;
using LuckArkman.XR.UI;
using LuckArkman.XR.Optimization;
using LuckArkman.XR.Voice;

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
        
        [Header("Módulo de Saída (Legado — AudioClips)")]
        [Tooltip("Sistema de saída por AudioClips. Usado apenas se voiceDirector for nulo.")]
        public Guia sistemaGuia;

        [Header("Sistema de Voz TTS (Feature 3)")]
        [Tooltip("VoiceDirectorService que gerencia a fila de fala priorizada via TTS Android.")]
        public VoiceDirectorService voiceDirector;

        [Header("Câmera Fallback (Feature 4)")]
        [Tooltip("Câmera do smartphone. Usada quando o óculos XR não está conectado.")]
        public SmartphoneCameraSource smartphoneCamera;

        [Header("Áudios de Sistema")]
        public AudioSource alertAudio;
        public AudioClip tutorialAudioClip;

        private SystemState currentState = SystemState.Idle;
        private float tempoDescansoManobra = 0f;
        private bool isSystemAudioPlaying = false; 

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
            // Executa o pipeline se o óculos está conectado OU se a câmera fallback está ativa
            bool xrAtivo = mjpegClient != null && mjpegClient.IsConnected;
            bool cameraAtiva = smartphoneCamera != null && smartphoneCamera.IsActive;

            if (currentState == SystemState.Active || xrAtivo || cameraAtiva)
            {
                RunActivePipeline();
            }
        }

        private void RunActivePipeline()
        {
            if (Time.time < tempoDescansoManobra) return;

            // === FEATURE 4: Seleção da fonte de vídeo ===
            // Prioridade 1: Stream do óculos XR | Prioridade 2: Câmera do smartphone
            Texture2D texAtiva = GetActiveVideoSource();
            if (texAtiva == null) return;

            // ==============================================================
            // ESCALONADOR DE 14 FRAMES (DIVISÃO DE CARGA NA GPU)
            // (3 MiDaS) -> (3 YOLO) -> (3 MiDaS) -> (3 YOLO) -> (1 MiDaS) -> (1 YOLO)
            // ==============================================================
            if (contadorFrames < 3 || (contadorFrames >= 6 && contadorFrames < 9) || contadorFrames == 12)
            {
                ultimoMidasData = midasAI.ExecuteInference(texAtiva);
            }
            else
            {
                ultimoYoloData = GetDetectionsFromAI(texAtiva);
            }

            contadorFrames++;
            if (contadorFrames >= 14) contadorFrames = 0;

            // ==============================================================
            // TOMADA DE DECISÃO
            // ==============================================================
            if (decisionMatrix != null)
            {
                Guia.EstadoInstrucao comandoFinal = decisionMatrix.AvaliarCenario(
                    ultimoYoloData,
                    ultimoMidasData,
                    texAtiva.width
                );

                if (!isSystemAudioPlaying)
                {
                    // === FEATURE 3: VoiceDirector tem prioridade sobre Guia legado ===
                    if (voiceDirector != null)
                    {
                        string textoComando = ConverterComandoParaTexto(comandoFinal, ultimoYoloData, ultimoMidasData);
                        if (!string.IsNullOrEmpty(textoComando))
                            voiceDirector.Enqueue(textoComando, VoicePriority.Obstacle);
                    }
                    else if (sistemaGuia != null)
                    {
                        // Fallback: sistema legado de AudioClips
                        sistemaGuia.ExecutarComando(comandoFinal);
                    }

                    bool isManobraEvasiva =
                        comandoFinal != Guia.EstadoInstrucao.Frente1 &&
                        comandoFinal != Guia.EstadoInstrucao.Frente2 &&
                        comandoFinal != Guia.EstadoInstrucao.Frente3 &&
                        comandoFinal != Guia.EstadoInstrucao.Frente4 &&
                        comandoFinal != Guia.EstadoInstrucao.Nenhum;

                    if (isManobraEvasiva)
                    {
                        tempoDescansoManobra = Time.time + 1.5f;
                        decisionMatrix.LimparBuffer();
                    }
                }
            }

            ProcessSystemSafety();
        }

        /// <summary>
        /// Retorna a textura ativa para o pipeline de IA.
        /// Prioridade 1: Stream MJPEG do óculos XR
        /// Prioridade 2: Câmera traseira do smartphone (Feature 4)
        /// </summary>
        private Texture2D GetActiveVideoSource()
        {
            if (mjpegClient != null &&
                mjpegClient.IsConnected &&
                mjpegClient.streamTexture != null &&
                mjpegClient.streamTexture.width > 32)
            {
                return mjpegClient.streamTexture;
            }

            if (smartphoneCamera != null &&
                smartphoneCamera.IsActive &&
                smartphoneCamera.CurrentFrame != null)
            {
                return smartphoneCamera.CurrentFrame;
            }

            return null;
        }

        /// <summary>
        /// Converte o enum de instrução em falas humanizadas dinâmicas,
        /// extraindo o nome do obstáculo (YOLO) e estimando a distância em passos (MiDaS).
        /// </summary>
        private string ConverterComandoParaTexto(Guia.EstadoInstrucao cmd, List<DetectionResult> yoloData, MidasResult midasData)
        {
            string nomeObstaculo = "obstáculo";
            
            // 1. Extrai o nome do objeto real classificado pela rede YOLO
            if (yoloData != null && yoloData.Count > 0)
            {
                nomeObstaculo = yoloData[0].label.ToLower();
            }
            
            // 2. Calcula a distância referencial em "Passos"
            // MiDaS DangerScore aumenta rapidamente perto da colisão (0 a 10+). 
            // Subtraímos de 12 para inverter o score (Maior Score = Menos Passos)
            int passos = Mathf.Clamp(12 - (int)midasData.dangerScore, 1, 15);
            
            // Capitaliza o nome (ex: "carro" -> "Carro")
            string capNome = char.ToUpper(nomeObstaculo[0]) + nomeObstaculo.Substring(1);

            // 3. Monta frases de navegação imersivas explicando como evitar o acidente
            switch (cmd)
            {
                case Guia.EstadoInstrucao.Parar:             
                    return $"Pare agora! {capNome} a {passos} passos à sua frente.";
                
                case Guia.EstadoInstrucao.GirarEsquerda:    
                    return $"Perigo! {capNome} bloqueando o caminho a {passos} passos. Gire totalmente para a esquerda para contornar.";
                
                case Guia.EstadoInstrucao.GirarDireita:     
                    return $"Perigo! {capNome} bloqueando o caminho a {passos} passos. Gire totalmente para a direita para contornar a área.";
                
                case Guia.EstadoInstrucao.DesviarEsquerda:  
                    return $"{capNome} identificado a {passos} passos de distância. Desvie para a esquerda para evitar uma colisão.";
                
                case Guia.EstadoInstrucao.DesviarDireita:   
                    return $"{capNome} identificado a {passos} passos de distância. Desvie para a direita para manter a segurança.";
                
                // Reduz spam auditivo em locais seguros (Frente3 e 4 omitidos)
                case Guia.EstadoInstrucao.Frente1:          return "Siga com extrema atenção.";
                case Guia.EstadoInstrucao.Frente2:          return "Avançando.";
                case Guia.EstadoInstrucao.Frente3:          return string.Empty;
                case Guia.EstadoInstrucao.Frente4:          return string.Empty;
                default:                                     return string.Empty;
            }
        }

        private List<DetectionResult> GetDetectionsFromAI(Texture2D sourceTexture)
        {
            if (yoloAI != null && sourceTexture != null)
            {
                List<DetectionResult> deteccoes = yoloAI.ExecuteInference(sourceTexture);
                if (deteccoes != null)
                    deteccoes.Sort((a, b) => (b.box.width * b.box.height).CompareTo(a.box.width * a.box.height));
                return deteccoes ?? new List<DetectionResult>();
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
            
            // NOVO: Desliga o HUD assim que conecta!
            if (hudController != null)
            {
                hudController.gameObject.SetActive(false);
            }
            
            // Notifica a conexão via TTS se disponível, senão usa AudioClip legado
            if (voiceDirector != null)
            {
                voiceDirector.Enqueue("Conexão estabelecida. Rota iniciada!", VoicePriority.System);
            }
            else if (alertAudio != null)
            {
                StartCoroutine(TocarSistemaDeAudioEmFila());
            }
        }

        private IEnumerator TocarSistemaDeAudioEmFila()
        {
            isSystemAudioPlaying = true;
            alertAudio.Play();

            float tempoBipe = (alertAudio.clip != null) ? alertAudio.clip.length : 1.0f;
            yield return new WaitForSeconds(tempoBipe);

            if (tutorialAudioClip != null) 
            {
                alertAudio.PlayOneShot(tutorialAudioClip);
                yield return new WaitForSeconds(tutorialAudioClip.length);
            }

            isSystemAudioPlaying = false;
        }

        public void OnConnectionLost()
        {
            SetState(SystemState.Searching);
            
            // NOVO: Religa o HUD se a conexão cair
            if (hudController != null)
            {
                hudController.gameObject.SetActive(true);
            }
            
            if (discoveryManager != null) discoveryManager.StartDiscovery();
        }
    }
}