using UnityEngine;
using UnityEngine.UIElements;
using LuckArkman.XR.Networking;
using System.Linq;


namespace LuckArkman.XR.UI
{
    public class HudController : MonoBehaviour
    {
        private UIDocument uiDocument;
        private VisualElement root;
        private ScrollView deviceList;
        private Label statusLabel;
        private VisualElement statusTag;

        [SerializeField] private WifiDiscoveryManager discoveryManager;
        [SerializeField] private MjpegTextureClient mjpegClient; 
        [SerializeField] private LatencyMonitor latencyMonitor;
        [SerializeField] private LuckArkman.XR.AI.YoloInferenceManager yoloAI;
        [SerializeField] private LuckArkman.XR.Safety.RiskCalculator riskCalculator;
        [SerializeField] private LuckArkman.XR.Safety.HeatmapManager heatmapManager;
        [SerializeField] private LuckArkman.XR.AR.ARCheckpointPlacer checkpointPlacer;


        private Label latencyLabel;
        private Label bitrateLabel;
        private Label arStatusLabel;
        private Label aiStatusLabel;
        private Button btnHotspot;

        // Botões do fluxo de navegação (spec App La Rosa)
        private Button btnEstabelecerCheckpoints;
        private Button btnMarcarCheckpoint;
        private Button btnIniciarNavegacao;
        private Label  lblTotalCheckpoints;
        
        private void OnEnable()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            root = uiDocument.rootVisualElement;
            if (root == null) return;

            deviceList = root.Q<ScrollView>("DeviceList");
            statusLabel = root.Q<Label>("StatusLabel");
            statusTag = root.Q<VisualElement>("StatusTag");
            latencyLabel = root.Q<Label>("LatencyValue");
            bitrateLabel = root.Q<Label>("BitrateValue");
            arStatusLabel = root.Q<Label>("ArStatusText");
            aiStatusLabel = root.Q<Label>("AiStatusText");

            if (discoveryManager != null)
            {
                discoveryManager.OnHeadsetFound -= UpdateDeviceList; 
                discoveryManager.OnHeadsetFound += UpdateDeviceList;
            }

            btnHotspot = root.Q<Button>("BtnHotspot");
            if (btnHotspot != null)
                btnHotspot.clicked += OpenHotspotSettings;

            // ── Botões de Navegação AR (spec App La Rosa) ────────────────────
            btnEstabelecerCheckpoints = root.Q<Button>("BtnEstabelecerCheckpoints");
            btnMarcarCheckpoint       = root.Q<Button>("BtnMarcarCheckpoint");
            btnIniciarNavegacao       = root.Q<Button>("BtnIniciarNavegacao");
            lblTotalCheckpoints       = root.Q<Label>("LblTotalCheckpoints");

            if (btnEstabelecerCheckpoints != null)
                btnEstabelecerCheckpoints.clicked += () =>
                {
                    checkpointPlacer?.AtivarModoMarcacao();
                    AtualizarBotoesNavegacao();
                };

            if (btnMarcarCheckpoint != null)
                btnMarcarCheckpoint.clicked += () =>
                {
                    checkpointPlacer?.MarcarCheckpointAtual();
                    AtualizarBotoesNavegacao();
                };

            if (btnIniciarNavegacao != null)
                btnIniciarNavegacao.clicked += () =>
                {
                    checkpointPlacer?.FinalizarEIniciarNavegacao();
                    AtualizarBotoesNavegacao();
                };

            // Estado inicial: apenas [Estabelecer Checkpoints] habilitado
            AtualizarBotoesNavegacao();

            if (checkpointPlacer != null)
                checkpointPlacer.OnCheckpointMarcado += _ => AtualizarBotoesNavegacao();
        }

        private void OpenHotspotSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        try 
                        {
                            using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent"))
                            {
                                intent.Call<AndroidJavaObject>("setClassName", "com.android.settings", "com.android.settings.TetherSettings");
                                currentActivity.Call("startActivity", intent);
                            }
                        }
                        catch 
                        {
                            // Fallback para a tela principal de configurações Wireless
                            using (AndroidJavaObject fallbackIntent = new AndroidJavaObject("android.content.Intent"))
                            {
                                fallbackIntent.Call<AndroidJavaObject>("setAction", "android.settings.WIRELESS_SETTINGS");
                                currentActivity.Call("startActivity", fallbackIntent);
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Hotspot] Falha geral ao invocar a tela de tethering: {e.Message}");
            }
#else
            Debug.Log("[Hotspot] O roteamento Wi-Fi interno pelo app só evoca a tela nativa no dispositivo Android.");
#endif
        }
        
        private void Update()
        {
            if (latencyLabel != null && latencyMonitor != null)
                latencyLabel.text = $"{latencyMonitor.LastRtt:F1} ms";

            if (arStatusLabel != null)
            {
                bool arAtivo = checkpointPlacer != null && checkpointPlacer.TemSuperficieDetectada;
                bool modoMarcacao = checkpointPlacer != null && checkpointPlacer.ModoMarcacaoAtivo;

                if (modoMarcacao && arAtivo)
                {
                    arStatusLabel.text = "📍 AR: SUPERFÍCIE DETECTADA — PRONTO PARA MARCAR";
                    arStatusLabel.style.color = new StyleColor(new Color(0.2f, 1f, 0.4f));
                }
                else if (modoMarcacao)
                {
                    arStatusLabel.text = "🔍 AR: AGUARDANDO SUPERFÍCIE...";
                    arStatusLabel.style.color = new StyleColor(new Color(1f, 0.6f, 0.1f));
                }
                else
                {
                    arStatusLabel.text = "ESPACIAL: MODO POCKET (AR em espera)";
                    arStatusLabel.style.color = new StyleColor(Color.gray);
                }
            }

            if (aiStatusLabel != null)
            {
                aiStatusLabel.text = "IA: PRONTA (SENTIS GPU)";
                aiStatusLabel.style.color = new StyleColor(new Color(0.22f, 0.74f, 0.97f)); 
            }
        }

        private void UpdateDeviceList()
        {
            if (deviceList == null) return;
            deviceList.Clear();

            foreach (var headset in discoveryManager.detectedHeadsets.Values)
            {
                deviceList.Add(CreateDeviceItem(headset.Name, headset.IP));
            }

            if (discoveryManager.detectedHeadsets.Count > 0 && statusLabel != null)
            {
                statusLabel.text = "DISPOSITIVOS ENCONTRADOS";
                statusTag?.AddToClassList("status-connected");
                statusTag?.RemoveFromClassList("status-searching");
            }
        }

        private VisualElement CreateDeviceItem(string name, string ip)
        {
            var container = new VisualElement();
            container.AddToClassList("device-item");

            var nameLabel = new Label($"{name} ({ip})");
            nameLabel.AddToClassList("device-name");

            var connectBtn = new Button(() => ConnectToHeadset(ip));
            connectBtn.text = "CONECTAR";
            connectBtn.AddToClassList("control-btn");

            container.Add(nameLabel);
            container.Add(connectBtn);

            return container;
        }

        private void ConnectToHeadset(string ip)
        {
            if (statusLabel != null) statusLabel.text = $"CONECTANDO A {ip}...";
            mjpegClient.Connect(ip);

            var orchestrator = FindFirstObjectByType<LuckArkman.XR.Main.MainSystemOrchestrator>();
            if (orchestrator != null && orchestrator.actuatorClient != null)
            {
                orchestrator.actuatorClient.SetTargetIp(ip);
                orchestrator.actuatorClient.SendConnectionSuccess();
            }
            
            if (statusTag != null) statusTag.style.backgroundColor = new StyleColor(Color.yellow);
        }

        /// <summary>
        /// Atualiza estado visual dos botões de navegação AR com base no estado atual do ARCheckpointPlacer.
        /// Regras:
        ///   [Estabelecer Checkpoints] — sempre habilitado (inicia/reinicia o modo)
        ///   [Marcar Checkpoint]        — habilitado apenas no modo de marcação ativo
        ///   [Iniciar]                  — habilitado quando há pelo menos 2 checkpoints marcados
        /// </summary>
        private void AtualizarBotoesNavegacao()
        {
            bool modoAtivo = checkpointPlacer != null && checkpointPlacer.ModoMarcacaoAtivo;
            int  total     = checkpointPlacer != null ? checkpointPlacer.TotalCheckpoints : 0;

            if (btnMarcarCheckpoint != null)
            {
                btnMarcarCheckpoint.SetEnabled(modoAtivo);
                btnMarcarCheckpoint.text = modoAtivo
                    ? $"📍 MARCAR CHECKPOINT ({total + 1})"
                    : "📍 MARCAR CHECKPOINT";
            }

            if (btnIniciarNavegacao != null)
                btnIniciarNavegacao.SetEnabled(total >= 2);

            if (lblTotalCheckpoints != null)
                lblTotalCheckpoints.text = $"Checkpoints marcados: {total}";

            if (btnEstabelecerCheckpoints != null)
                btnEstabelecerCheckpoints.text = modoAtivo ? "✏️ MARCANDO ROTA..." : "🗺️ ESTABELECER CHECKPOINTS";
        }

        public void SetVisibility(bool isVisible)
        {
            if (root != null)
            {
                root.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}