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
        [Header("Configurações de Rede")]
        // ANTES: public int discoveryPort = 8888;
        public int discoveryPort = 4444; 
        
        // ANTES: public string broadcastMessage = "XR_HEADSET_DISCOVERY";
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
            // --- INJEÇÃO DIRETA DO IP (O BYPASS) ---
            string ipFixo = "192.168.17.102";
            
            detectedHeadsets[ipFixo] = new HeadsetInfo 
            { 
                Name = "Óculos La Rosa (VIP)", 
                IP = ipFixo, 
                LastSeen = DateTime.Now 
            };
            
            Debug.Log($"[WifiDiscovery] BYPASS ATIVADO: Injetando o IP fixo {ipFixo} na lista.");
            
            // Avisa o HudController para criar o botão na tela imediatamente!
            OnHeadsetFound?.Invoke();

            // ----------------------------------------
            
            // Mantemos a função original ligada apenas por segurança
            StartDiscovery();
        }

        public void StartDiscovery()
        {
            // CORREÇÃO: Verifica se a porta já está sendo ouvida. Se estiver, não faz nada!
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
            }
            catch (Exception e)
            {
                // Se der erro mesmo assim, garantimos que a variável seja limpa
                Debug.LogError($"[WifiDiscovery] Falha ao iniciar UDP: {e.Message}");
                if (udpListener != null)
                {
                    udpListener.Close();
                    udpListener = null;
                }
            }
        }

        private void Update()
        {
            if (udpListener == null) return;

            while (udpListener.Available > 0)
            {
                byte[] bytes = udpListener.Receive(ref groupEP);
                string message = Encoding.UTF8.GetString(bytes);
                
                if (message.StartsWith(broadcastMessage))
                {
                    ProcessDiscoveryMessage(message, groupEP.Address.ToString());
                }
            }
        }

        private void ProcessDiscoveryMessage(string msg, string ip)
        {
            // ANTES: string[] parts = msg.Split('|');
            // ANTES: string deviceName = parts.Length > 1 ? parts[1] : "Oculus Unknown";
            
            // AGORA: Definimos um nome fixo, pois o ESP32 manda apenas o IP, sem o separador '|'
            string deviceName = "Óculos La Rosa"; 

            if (!detectedHeadsets.ContainsKey(ip))
            {
                detectedHeadsets[ip] = new HeadsetInfo 
                { 
                    Name = deviceName, 
                    IP = ip, 
                    LastSeen = DateTime.Now 
                };
                
                // ANTES: Debug.Log($"[WifiDiscovery] Novo Headset encontrado: {deviceName} em {ip}");
                Debug.Log($"[WifiDiscovery] Novo dispositivo encontrado: {deviceName} em {ip}");
                
                OnHeadsetFound?.Invoke();
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
            udpListener?.Close();
        }
    }
}