using UnityEngine;

namespace LuckArkman.XR.UI
{
    /// <summary>
    /// Provê a câmera traseira do smartphone como fonte de vídeo alternativa ao MJPEG.
    ///
    /// Uso:
    ///   Quando o óculos XR não está disponível, o usuário coloca o smartphone em um
    ///   suporte/adaptador e usa a câmera traseira para identificar obstáculos.
    ///   O MainSystemOrchestrator.GetActiveVideoSource() escolhe automaticamente
    ///   entre esta câmera e o stream do óculos XR.
    ///
    /// Compatibilidade:
    ///   Usa WebCamTexture (API nativa Unity), funciona em Android e iOS.
    ///   Requer permissão CAMERA no AndroidManifest.xml (já declarada).
    /// </summary>
    public class SmartphoneCameraSource : MonoBehaviour
    {
        [Header("Configuração")]
        [Tooltip("True = câmera traseira (recomendado para obstáculos). False = frontal.")]
        public bool useBackCamera = true;

        [Tooltip("Largura do frame capturado. 640 é o tamanho do tensor YOLO/MiDaS.")]
        public int targetWidth = 640;

        [Tooltip("Altura do frame capturado.")]
        public int targetHeight = 480;

        [Tooltip("Taxa de quadros alvo para a WebCamTexture.")]
        public int targetFPS = 30;

        /// <summary>True quando a câmera está ativa e capturando frames.</summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Frame atual em Texture2D, compatível com YoloInferenceManager e MidasInferenceManager.
        /// Atualizado a cada frame que a WebCamTexture reporta novo dado (didUpdateThisFrame).
        /// </summary>
        public Texture2D CurrentFrame { get; private set; }

        private WebCamTexture _webCamTexture;
        private RenderTexture _renderTexture;

        // ─── API Pública ──────────────────────────────────────────────────

        /// <summary>Ativa a câmera do smartphone e começa a capturar frames.</summary>
        public void Activate()
        {
            if (IsActive)
            {
                Debug.LogWarning("[SmartphoneCamera] Já está ativa. Ignorando Activate().");
                return;
            }

            if (WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("[SmartphoneCamera] Nenhuma câmera encontrada no dispositivo.");
                return;
            }

            string deviceName = FindCameraDevice(useBackCamera);
            if (deviceName == null)
            {
                Debug.LogError("[SmartphoneCamera] Câmera solicitada não encontrada. " +
                               "Verifique permissão CAMERA no manifest.");
                return;
            }

            // WebCamTexture é a API Unity para câmera do dispositivo.
            _webCamTexture = new WebCamTexture(deviceName, targetWidth, targetHeight, targetFPS);
            _webCamTexture.Play();

            // RenderTexture intermediária para a conversão eficiente WebCam → Texture2D.
            _renderTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);

            // Texture2D que será exposta ao pipeline de IA.
            CurrentFrame = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);

            IsActive = true;
            Debug.Log($"[SmartphoneCamera] Câmera '{deviceName}' ativada " +
                      $"({targetWidth}x{targetHeight} @ {targetFPS}fps).");
        }

        /// <summary>Para a câmera e libera todos os recursos.</summary>
        public void Deactivate()
        {
            if (!IsActive) return;

            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (CurrentFrame != null)
            {
                Destroy(CurrentFrame);
                CurrentFrame = null;
            }

            IsActive = false;
            Debug.Log("[SmartphoneCamera] Câmera desativada e recursos liberados.");
        }

        // ─── Ciclo de Vida ────────────────────────────────────────────────

        private void Update()
        {
            if (!IsActive || _webCamTexture == null) return;

            // Só atualiza a Texture2D quando a WebCamTexture reporta um novo frame.
            // Evita trabalho desnecessário em frames onde a câmera não atualizou.
            if (!_webCamTexture.didUpdateThisFrame) return;

            UpdateSnapshot();
        }

        private void OnDestroy()
        {
            Deactivate();
        }

        // ─── Internos ─────────────────────────────────────────────────────

        /// <summary>
        /// Copia o frame atual da WebCamTexture para a Texture2D via RenderTexture.
        /// Pipeline: WebCamTexture → Graphics.Blit → RenderTexture → ReadPixels → Texture2D
        ///
        /// Por que RenderTexture intermediária?
        ///   - Graphics.Blit opera na GPU (mais eficiente que GetPixels/SetPixels na CPU).
        ///   - ReadPixels transfere do RenderTexture para a RAM (necessário para Texture2D).
        ///   - Apenas os frames com didUpdateThisFrame=true passam por este pipeline.
        /// </summary>
        private void UpdateSnapshot()
        {
            // Blit da WebCamTexture para a RenderTexture (operação GPU)
            Graphics.Blit(_webCamTexture, _renderTexture);

            // Salva e muda o RenderTexture ativo para poder fazer ReadPixels
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = _renderTexture;

            // ReadPixels copia do RenderTexture ativo para a Texture2D (CPU)
            // false no último parâmetro = não recalcula mipmap (mais rápido)
            CurrentFrame.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0, false);
            CurrentFrame.Apply(false);

            // Restaura o RenderTexture ativo anterior
            RenderTexture.active = previousActive;
        }

        /// <summary>
        /// Encontra o nome do dispositivo de câmera com a orientação solicitada.
        /// Retorna o nome da câmera traseira se backFacing=true, frontal se false.
        /// Faz fallback para qualquer câmera disponível se a orientação não for encontrada.
        /// </summary>
        private string FindCameraDevice(bool backFacing)
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            string fallback = null;

            foreach (var device in devices)
            {
                // isFrontFacing=true → câmera frontal; false → traseira
                bool isFront = device.isFrontFacing;
                bool isDesiredBack = backFacing && !isFront;
                bool isDesiredFront = !backFacing && isFront;

                if (isDesiredBack || isDesiredFront)
                    return device.name;

                // Guarda a primeira disponível como fallback
                if (fallback == null)
                    fallback = device.name;
            }

            // Se não encontrou o tipo desejado, usa qualquer câmera disponível
            if (fallback != null)
                Debug.LogWarning("[SmartphoneCamera] Câmera solicitada não encontrada. " +
                                 $"Usando fallback: '{fallback}'.");
            return fallback;
        }
    }
}
