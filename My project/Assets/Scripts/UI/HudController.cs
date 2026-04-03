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
        
        [Header("Novas Features (Sprint 7)")]
        [SerializeField] private SmartphoneCameraSource smartphoneCamera;
        [SerializeField] private LuckArkman.XR.Navigation.GoogleMapsNavigator mapsNavigator;

        private Label latencyLabel;
        private Label bitrateLabel;
        private Label arStatusLabel;
        private Label aiStatusLabel;

        // UI Builder Elements - Feature 1 & 4
        private Toggle toggleCameraSource;
        private TextField destinationInput;
        private Button navigateButton;
        
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

            // Elementos de UI das novas features
            toggleCameraSource = root.Q<Toggle>("ToggleCameraSource");
            destinationInput = root.Q<TextField>("DestinationInput");
            navigateButton = root.Q<Button>("NavigateButton");

            if (discoveryManager != null)
            {
                discoveryManager.OnHeadsetFound -= UpdateDeviceList; 
                discoveryManager.OnHeadsetFound += UpdateDeviceList;
            }

            // Bindings de Evento
            if (toggleCameraSource != null)
            {
                toggleCameraSource.RegisterValueChangedCallback(evt => OnCameraToggleChanged(evt.newValue));
            }

            if (navigateButton != null && destinationInput != null)
            {
                navigateButton.clicked += OnNavigateClicked;
            }
        }
        
        private void Update()
        {
            if (latencyLabel != null && latencyMonitor != null)
                latencyLabel.text = $"{latencyMonitor.LastRtt:F1} ms";

            if (arStatusLabel != null)
            {
                arStatusLabel.text = "ESPACIAL: DESATIVADO (MODO POCKET)";
                arStatusLabel.style.color = new StyleColor(Color.gray);
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

        // ==========================================
        // HANDLERS DAS NOVAS FEATURES (SPRINT 7)
        // ==========================================

        private void OnCameraToggleChanged(bool useSmartphoneCamera)
        {
            if (smartphoneCamera == null)
            {
                Debug.LogWarning("[MainHUD] Referência 'smartphoneCamera' não vinculada no Inspector.");
                return;
            }

            if (useSmartphoneCamera)
            {
                smartphoneCamera.Activate();
                if (statusLabel != null) statusLabel.text = "CÂMERA ATIVA";
            }
            else
            {
                smartphoneCamera.Deactivate();
                if (statusLabel != null) statusLabel.text = "PROCURANDO ÓCULOS...";
            }
        }

        private void OnNavigateClicked()
        {
            if (mapsNavigator == null)
            {
                Debug.LogWarning("[MainHUD] Referência 'mapsNavigator' não vinculada no Inspector.");
                return;
            }

            string dest = destinationInput?.value;
            if (!string.IsNullOrWhiteSpace(dest))
            {
                if (statusLabel != null) statusLabel.text = $"NAVEGANDO PARA: {dest.ToUpper()}";
                mapsNavigator.StartNavigation(dest);
            }
        }
    }
}