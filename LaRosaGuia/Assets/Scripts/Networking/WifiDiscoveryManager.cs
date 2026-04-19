using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using System.Collections.Generic;

namespace LuckArkman.XR.Networking
{
    public class WifiDiscoveryManager : MonoBehaviour
    {
        [Header("Modo de Teste Rápido (Bypass)")]
        [Tooltip("Marque para testar sem o óculos físico ligado")]
        public bool usarIpFixo = false;
        public string ipFixo = "192.168.43.50";

        [Header("Configurações de Rede")]
        public int discoveryPort = 4444; 
        public string broadcastMessage = "LAROSA_IP:"; 
        
        private UdpClient udpListener;
        private IPEndPoint groupEP;
        
        public struct HeadsetInfo
        {
            public string Name;
            public string IP;
            public DateTime LastSeen;
        }

        public Dictionary<string, HeadsetInfo> detectedHeadsets = new Dictionary<string, HeadsetInfo>();
        
        public event Action OnHeadsetFound;
        
        private void Start()
        {
            if (usarIpFixo)
            {
                Debug.Log($"[WifiDiscovery] MODO DESENVOLVEDOR: Injetando IP {ipFixo}");
                
                detectedHeadsets[ipFixo] = new HeadsetInfo 
                { 
                    Name = "Óculos La Rosa (Bypass)", 
                    IP = ipFixo, 
                    LastSeen = DateTime.Now 
                };
                
                // Avisa a UI para desenhar o botão imediatamente
                OnHeadsetFound?.Invoke();
            }
            else
            {
                // Modo Produção: Vai pra rua e procura o grito do ESP32
                StartDiscovery();
            }
        }

        public void StartDiscovery()
        {
            if (udpListener != null)
            {
                Debug.Log($"[WifiDiscovery] O sistema já está ouvindo a porta {discoveryPort}. Ignorando nova chamada.");
                return; 
            }

            try
            {
                udpListener = new UdpClient(discoveryPort);
                groupEP = new IPEndPoint(IPAddress.Any, discoveryPort);
                Debug.Log($"[WifiDiscovery] Ouvindo na porta {discoveryPort}...");
                
                // OTIMIZAÇÃO: Começa a escutar o UDP de forma assíncrona (em segundo plano)
                udpListener.BeginReceive(new AsyncCallback(ReceiveCallback), null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WifiDiscovery] Falha ao iniciar UDP: {e.Message}");
                if (udpListener != null)
                {
                    udpListener.Close();
                    udpListener = null;
                }
            }
        }

        // Esta função roda invisível no fundo, sem derrubar o FPS da Unity
        private void ReceiveCallback(IAsyncResult ar)
        {
            if (udpListener == null) return;

            try
            {
                byte[] bytes = udpListener.EndReceive(ar, ref groupEP);
                string message = Encoding.UTF8.GetString(bytes);
                
                if (message.StartsWith(broadcastMessage))
                {
                    // Como estamos em outra Thread, precisamos passar os dados para processamento
                    ProcessDiscoveryMessage(message, groupEP.Address.ToString());
                }

                // Volta a escutar o próximo pacote da rede
                udpListener.BeginReceive(new AsyncCallback(ReceiveCallback), null);
            }
            catch (ObjectDisposedException) { /* Ignora se o Listener foi fechado ao fechar o app */ }
            catch (Exception e) { Debug.LogError($"[WifiDiscovery] Erro no Receive: {e.Message}"); }
        }

        private void ProcessDiscoveryMessage(string msg, string ip)
        {
            string deviceName = "Óculos La Rosa"; 

            if (!detectedHeadsets.ContainsKey(ip))
            {
                detectedHeadsets[ip] = new HeadsetInfo 
                { 
                    Name = deviceName, 
                    IP = ip, 
                    LastSeen = DateTime.Now 
                };
                
                Debug.Log($"[WifiDiscovery] Novo dispositivo encontrado: {deviceName} em {ip}");
                
                // Como não podemos desenhar botões direto de uma Thread de fundo, mandamos a Unity fazer isso no próximo frame
                UnityMainThreadDispatcher.Instance().Enqueue(() => OnHeadsetFound?.Invoke());
            }
            else
            {
                var info = detectedHeadsets[ip];
                info.LastSeen = DateTime.Now;
                detectedHeadsets[ip] = info;
            }
        }

        private void OnDestroy()
        {
            if (udpListener != null)
            {
                udpListener.Close();
                udpListener = null;
            }
        }
    }
}