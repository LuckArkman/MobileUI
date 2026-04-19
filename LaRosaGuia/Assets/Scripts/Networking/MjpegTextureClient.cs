using System;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace LuckArkman.XR.Networking
{
    /// <summary>
    /// Cliente HTTP dedicado a ler o stream MJPEG (multipart/x-mixed-replace) da Porta 80 do ESP32-CAM.
    /// Opera em uma Thread em background para evitar travamentos na Unity.
    /// </summary>
    public class MjpegTextureClient : MonoBehaviour
    {
        [Header("Saída de Vídeo")]
        [Tooltip("A textura onde o frame do ESP32 será desenhado.")]
        public Texture2D streamTexture;

        private Thread streamThread;
        private bool isRunning = false;
        
        // Buffers para transferência segura entre a Thread de rede e a Unity Main Thread
        private byte[] currentFrameBytes;
        private bool isNewFrameAvailable = false;
        private readonly object frameLock = new object();

        // Controle de Estado para a UI
        public bool IsConnected { get; private set; }
        private bool wasConnected = false; // Auxiliar para disparar eventos na Main Thread

        public event Action OnConnected;
        public event Action OnDisconnected;

        public void Connect(string ip)
        {
            if (isRunning) return;
            
            // Inicializa a textura se estiver nula (o tamanho será ajustado dinamicamente pelo LoadImage)
            if (streamTexture == null) 
            {
                streamTexture = new Texture2D(400, 296, TextureFormat.RGB24, false);
            }
            
            string url = $"http://{ip}:80/stream";
            Debug.Log($"[MjpegClient] Tentando conectar ao stream em: {url}");
            
            isRunning = true;
            
            // Inicia a Thread de background para ler o Wi-Fi sem congelar a tela do celular
            streamThread = new Thread(() => ReadStream(url)) { IsBackground = true };
            streamThread.Start();
        }

        private void ReadStream(string url)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 5000;
                
                // O SEGREDO 1: Proíbe a Unity de tentar "baixar o arquivo inteiro"
                request.AllowReadStreamBuffering = false;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    IsConnected = true;
                    Debug.Log("[MjpegClient] CONEXÃO ESTABELECIDA! Lendo o vídeo em tempo real...");

                    // O SEGREDO 2: Ler em blocos de 8KB (MUITO mais rápido que 1 por 1 byte)
                    byte[] chunk = new byte[8192]; 
                    int bytesRead;
                    int lastByte = -1;
                    bool inFrame = false;
                    MemoryStream frameBuffer = new MemoryStream();

                    while (isRunning && (bytesRead = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        for (int i = 0; i < bytesRead; i++)
                        {
                            int b = chunk[i];

                            // Achou o cabeçalho da foto JPEG (FF D8)
                            if (lastByte == 0xFF && b == 0xD8)
                            {
                                inFrame = true;
                                frameBuffer.SetLength(0); // Limpa a foto anterior
                                frameBuffer.WriteByte((byte)lastByte);
                            }

                            if (inFrame)
                            {
                                frameBuffer.WriteByte((byte)b);

                                // Achou o final da foto JPEG (FF D9)
                                if (lastByte == 0xFF && b == 0xD9)
                                {
                                    inFrame = false;
                                    
                                    // Trava a memória para atualizar a textura na Unity
                                    lock (frameLock)
                                    {
                                        currentFrameBytes = frameBuffer.ToArray();
                                        isNewFrameAvailable = true;
                                    }
                                }
                            }
                            lastByte = b;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MjpegClient] Conexão encerrada ou falha na rede: {e.Message}");
            }
            finally
            {
                IsConnected = false;
                isRunning = false;
            }
        }

        private void Update()
        {
            // --- GERENCIAMENTO DE EVENTOS DA UI ---
            if (IsConnected && !wasConnected)
            {
                wasConnected = true;
                OnConnected?.Invoke();
            }
            else if (!IsConnected && wasConnected)
            {
                wasConnected = false;
                OnDisconnected?.Invoke();
            }

            // --- RENDERIZAÇÃO DO FRAME ---
            // A textura só pode ser atualizada na Main Thread da Unity
            if (isNewFrameAvailable)
            {
                lock (frameLock)
                {
                    if (currentFrameBytes != null && currentFrameBytes.Length > 0)
                    {
                        // Proteção extra: garante que a textura exista na memória
                        if (streamTexture == null)
                        {
                            streamTexture = new Texture2D(2, 2);
                        }

                        // Tenta converter os bytes em imagem visual
                        bool success = streamTexture.LoadImage(currentFrameBytes);
                        
                        // Diagnóstico poderoso: avisa se a conversão deu certo ou errado
                        if (success)
                        {
                            Debug.Log($"[MjpegClient] FOTO DECODIFICADA! Tamanho: {currentFrameBytes.Length} bytes.");
                        }
                        else
                        {
                            Debug.LogWarning("[MjpegClient] Falha ao decodificar os bytes da imagem. O frame pode estar corrompido.");
                        }
                    }
                    isNewFrameAvailable = false;
                }
            }
        }

        public void Disconnect()
        {
            isRunning = false;
            IsConnected = false;
        }

        private void OnDestroy()
        {
            Disconnect();
            // Garante que a Thread de background seja limpa ao fechar o app
            streamThread?.Join(500); 
        }
    }
}