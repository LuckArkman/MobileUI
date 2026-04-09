using UnityEngine;
#if UNITY_ANDROID || UNITY_IOS
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

namespace LuckArkman.XR.UI
{
    /// <summary>
    /// Provê a câmera traseira do smartphone como fonte de vídeo alternativa.
    ///
    /// RESOLUÇÃO DO CONFLITO DE HARDWARE (Briga de Câmera):
    /// O Android 14 mata o app se o ARCore (ARFoundation) e a WebCamTexture
    /// tentarem assumir o hardware 'camera' simultaneamente.
    /// Esta versão atualizada detecta se o ARCameraManager está presente na cena.
    ///   - Se estiver: Extrai o frame (XRCpuImage) direto do ARCore (Zero conflito, performance máxima).
    ///   - Se NÃO estiver: Usa o WebCamTexture padrão como fallback de segurança.
    /// </summary>
    public class SmartphoneCameraSource : MonoBehaviour
    {
        [Header("Configuração de Fallback (Apenas se ARCore estiver offline)")]
        [Tooltip("True = câmera traseira (recomendado para obstáculos). False = frontal.")]
        public bool useBackCamera = true;
        public int targetWidth = 640;
        public int targetHeight = 480;
        public int targetFPS = 30;

        /// <summary>True quando a câmera está ativa e capturando frames.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Frame atual em Texture2D, exposto ao Yolo e MiDaS.</summary>
        public Texture2D CurrentFrame { get; private set; }

#if UNITY_ANDROID || UNITY_IOS
        private ARCameraManager _arCameraManager;
#endif

        private WebCamTexture _webCamTexture;
        private RenderTexture _renderTexture;
        private bool _usingARCoreProvider = false;

        // ─── API Pública ──────────────────────────────────────────────────

        public void Activate()
        {
            if (IsActive) return;

#if UNITY_ANDROID || UNITY_IOS
            // Tenta encontrar o gerenciador de câmera do ARCore na cena
            _arCameraManager = FindFirstObjectByType<ARCameraManager>();
            
            if (_arCameraManager != null)
            {
                Debug.Log("[SmartphoneCamera] ARCore detectado! Assumindo frames via XRCpuImage para evitar briga de hardware.");
                _usingARCoreProvider = true;
                _arCameraManager.frameReceived += OnARCameraFrameReceived;
                IsActive = true;
                return;
            }
#endif

            // Fallback para WebCamTexture (Se o ARCore não estiver na cena)
            Debug.LogWarning("[SmartphoneCamera] ARCore NÃO detectado. Iniciando WebCamTexture local.");
            _usingARCoreProvider = false;
            StartWebCamTexture();
        }

        public void Deactivate()
        {
            if (!IsActive) return;
            IsActive = false;

#if UNITY_ANDROID || UNITY_IOS
            if (_usingARCoreProvider && _arCameraManager != null)
            {
                _arCameraManager.frameReceived -= OnARCameraFrameReceived;
                _arCameraManager = null;
            }
#endif

            if (!_usingARCoreProvider)
            {
                StopWebCamTexture();
            }

            if (CurrentFrame != null)
            {
                Destroy(CurrentFrame);
                CurrentFrame = null;
            }

            Debug.Log("[SmartphoneCamera] Câmera desativada e recursos liberados.");
        }

        // ─── Lógica ARCore (Sem Conflito) ──────────────────────────────────

#if UNITY_ANDROID || UNITY_IOS
        private void OnARCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            if (!IsActive || _arCameraManager == null) return;

            // Extrai a imagem crua fornecida pelo ARCore que já tem o Lock do hardware
            if (!_arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
                return;

            try
            {
                var format = TextureFormat.RGBA32;
                if (CurrentFrame == null || CurrentFrame.width != image.width || CurrentFrame.height != image.height)
                {
                    CurrentFrame = new Texture2D(image.width, image.height, format, false);
                }

                // Configura a conversão para nossa Texture2D processável
                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(image.width, image.height),
                    outputFormat = format,
                    transformation = XRCpuImage.Transformation.None
                };

                // Executa a conversão dos bytes diretamente para a Textura do Unity
                var rawTextureData = CurrentFrame.GetRawTextureData<byte>();
                image.Convert(conversionParams, rawTextureData);
                CurrentFrame.Apply();
            }
            finally
            {
                // IMPORTANTÍSSIMO: Evita vazamento de memória liberando a imagem nativa
                image.Dispose();
            }
        }
#endif

        // ─── Lógica Fallback WebCamTexture ────────────────────────────────

        private void StartWebCamTexture()
        {
            if (WebCamTexture.devices.Length == 0) return;

            string deviceName = FindCameraDevice(useBackCamera);
            if (deviceName == null) return;

            _webCamTexture = new WebCamTexture(deviceName, targetWidth, targetHeight, targetFPS);
            _webCamTexture.Play();

            _renderTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            CurrentFrame = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            IsActive = true;
        }

        private void StopWebCamTexture()
        {
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
        }

        private void Update()
        {
            if (!IsActive || _usingARCoreProvider || _webCamTexture == null) return;

            if (!_webCamTexture.didUpdateThisFrame) return;

            Graphics.Blit(_webCamTexture, _renderTexture);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            CurrentFrame.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0, false);
            CurrentFrame.Apply(false);
            RenderTexture.active = previousActive;
        }

        private string FindCameraDevice(bool backFacing)
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            string fallback = null;

            foreach (var device in devices)
            {
                bool isFront = device.isFrontFacing;
                if ((backFacing && !isFront) || (!backFacing && isFront))
                    return device.name;
                if (fallback == null) fallback = device.name;
            }
            return fallback;
        }

        private void OnDestroy()
        {
            Deactivate();
        }
    }
}
