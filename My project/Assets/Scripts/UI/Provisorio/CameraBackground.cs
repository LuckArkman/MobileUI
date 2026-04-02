using UnityEngine;
using UnityEngine.UI;
using LuckArkman.XR.Networking;

namespace LuckArkman.XR.UI
{
    [RequireComponent(typeof(RawImage))]
    public class CameraBackground : MonoBehaviour
    {
        [Header("Fonte do Vídeo")]
        public MjpegTextureClient mjpegClient;

        private RawImage backgroundImage;

        void Start()
        {
            backgroundImage = GetComponent<RawImage>();
            
            // Tenta achar o cliente automaticamente caso você esqueça de arrastar
            if (mjpegClient == null)
                mjpegClient = FindFirstObjectByType<MjpegTextureClient>();
        }

        void Update()
        {
            if (mjpegClient != null && mjpegClient.streamTexture != null)
            {
                backgroundImage.texture = mjpegClient.streamTexture;
                
                // Liga o fundo apenas quando tiver vídeo
                if (!backgroundImage.enabled) backgroundImage.enabled = true;
            }
            else
            {
                // Deixa a tela transparente/escura enquanto não conectar
                if (backgroundImage.enabled) backgroundImage.enabled = false;
            }
        }
    }
}