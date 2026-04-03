using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LuckArkman.XR.Navigation;
using LuckArkman.XR.Voice;
using LuckArkman.XR.UI;

namespace LuckArkman.XR.UI
{
    /// <summary>
    /// Painel de entrada de destino para o Google Maps.
    ///
    /// Funcionalidades:
    ///   - Campo de texto onde o usuário digita o destino.
    ///   - Botão "Navegar" que aciona o GoogleMapsNavigator.
    ///   - Botão "Cancelar" que encerra a navegação ativa.
    ///   - Botão "Câmera" que ativa/desativa a SmartphoneCameraSource (Feature 4).
    ///   - Label de status mostrando o destino atual.
    ///   - O painel se oculta automaticamente ao iniciar a navegação
    ///     (o áudio assume o papel de feedback principal).
    ///
    /// Configuração na cena:
    ///   Adicione ao Canvas principal e preencha as referências no Inspector.
    /// </summary>
    public class DestinationInputUI : MonoBehaviour
    {
        [Header("Componentes de UI")]
        [Tooltip("Campo de texto onde o usuário digita o destino.")]
        public TMP_InputField inputDestination;

        [Tooltip("Botão para iniciar a navegação com o destino inserido.")]
        public Button btnNavigate;

        [Tooltip("Botão para cancelar a navegação em andamento.")]
        public Button btnCancel;

        [Tooltip("Botão para ativar/desativar a câmera do smartphone (Feature 4).")]
        public Button btnToggleCamera;

        [Tooltip("Label que mostra o destino atual ou instrução ao usuário.")]
        public TextMeshProUGUI lblStatus;

        [Tooltip("Painel raiz desta UI. Ocultado durante a navegação.")]
        public GameObject panelRoot;

        [Header("Referências de Sistema")]
        public GoogleMapsNavigator mapsNavigator;
        public SmartphoneCameraSource smartphoneCamera;
        public VoiceDirectorService voiceDirector;

        private bool _cameraActive;

        // ─── Ciclo de Vida ────────────────────────────────────────────────

        private void Start()
        {
            ValidateReferences();
            RegisterButtonListeners();
            RefreshUI();
        }

        private void Update()
        {
            // Atualiza o label de status em tempo real
            if (lblStatus == null) return;

            if (mapsNavigator != null && mapsNavigator.IsNavigating)
            {
                lblStatus.text = $"Navegando para: {mapsNavigator.CurrentDestination}";
            }
            else
            {
                lblStatus.text = _cameraActive
                    ? "Câmera ativa — Posicione o smartphone no suporte."
                    : "Onde você deseja ir?";
            }
        }

        // ─── Inicialização ────────────────────────────────────────────────

        private void ValidateReferences()
        {
            if (inputDestination == null)
                Debug.LogError("[DestinationUI] inputDestination não configurado no Inspector!");
            if (btnNavigate == null)
                Debug.LogError("[DestinationUI] btnNavigate não configurado no Inspector!");
            if (mapsNavigator == null)
                Debug.LogWarning("[DestinationUI] mapsNavigator não configurado — navegação desativada.");
        }

        private void RegisterButtonListeners()
        {
            if (btnNavigate != null)
                btnNavigate.onClick.AddListener(OnBtnNavigateClicked);

            if (btnCancel != null)
                btnCancel.onClick.AddListener(OnBtnCancelClicked);

            if (btnToggleCamera != null)
                btnToggleCamera.onClick.AddListener(OnBtnToggleCameraClicked);
        }

        private void RefreshUI()
        {
            // Estado inicial: botão Cancelar invisível
            if (btnCancel != null)
                btnCancel.gameObject.SetActive(false);
        }

        // ─── Handlers dos Botões ─────────────────────────────────────────

        private void OnBtnNavigateClicked()
        {
            if (inputDestination == null || mapsNavigator == null) return;

            string destination = inputDestination.text.Trim();

            if (string.IsNullOrEmpty(destination))
            {
                voiceDirector?.Enqueue(
                    "Por favor, escreva o nome do lugar para onde você quer ir.",
                    VoicePriority.System);
                return;
            }

            mapsNavigator.StartNavigation(destination);

            // Mostra Cancelar, esconde Navegar e oculta o painel de entrada
            if (btnNavigate != null) btnNavigate.gameObject.SetActive(false);
            if (btnCancel  != null) btnCancel.gameObject.SetActive(true);
            if (panelRoot  != null) panelRoot.SetActive(false);
        }

        private void OnBtnCancelClicked()
        {
            mapsNavigator?.StopNavigation();

            // Restaura os botões e exibe o painel
            if (btnNavigate != null) btnNavigate.gameObject.SetActive(true);
            if (btnCancel   != null) btnCancel.gameObject.SetActive(false);
            if (panelRoot   != null) panelRoot.SetActive(true);

            if (inputDestination != null)
                inputDestination.text = string.Empty;
        }

        private void OnBtnToggleCameraClicked()
        {
            if (smartphoneCamera == null)
            {
                Debug.LogWarning("[DestinationUI] SmartphoneCameraSource não configurado.");
                return;
            }

            if (_cameraActive)
            {
                smartphoneCamera.Deactivate();
                _cameraActive = false;
                voiceDirector?.Enqueue("Câmera do celular desativada.", VoicePriority.System);

                // Atualiza o label do botão se houver TextMeshPro no botão
                var btnLabel = btnToggleCamera.GetComponentInChildren<TextMeshProUGUI>();
                if (btnLabel != null) btnLabel.text = "Usar Câmera do Celular";
            }
            else
            {
                smartphoneCamera.Activate();
                _cameraActive = true;
                voiceDirector?.Enqueue(
                    "Câmera do celular ativada. Posicione o dispositivo no suporte.",
                    VoicePriority.System);

                var btnLabel = btnToggleCamera.GetComponentInChildren<TextMeshProUGUI>();
                if (btnLabel != null) btnLabel.text = "Desativar Câmera";
            }
        }

        private void OnDestroy()
        {
            // Remove listeners para evitar erros de referência após destruição
            if (btnNavigate     != null) btnNavigate.onClick.RemoveListener(OnBtnNavigateClicked);
            if (btnCancel       != null) btnCancel.onClick.RemoveListener(OnBtnCancelClicked);
            if (btnToggleCamera != null) btnToggleCamera.onClick.RemoveListener(OnBtnToggleCameraClicked);
        }
    }
}
