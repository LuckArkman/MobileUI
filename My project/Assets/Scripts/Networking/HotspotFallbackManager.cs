using UnityEngine;
using LuckArkman.XR.Voice;
#if UNITY_ANDROID
using System;
#endif

namespace LuckArkman.XR.Networking
{
    /// <summary>
    /// Gerencia o fallback de conectividade: Wi-Fi Local → Hotspot do Smartphone.
    ///
    /// Fluxo:
    ///   1. WifiDiscoveryManager tenta encontrar o óculos XR na rede Wi-Fi local (UDP).
    ///   2. Se nenhum headset for encontrado em `timeoutSeconds`, este componente
    ///      ativa o Android Local-Only Hotspot via HotspotManager.java.
    ///   3. O usuário é guiado por voz para conectar o óculos ao hotspot.
    ///   4. O WifiDiscoveryManager continua escutando e detecta o óculos quando ele
    ///      se conectar ao hotspot (mesmo endereço UDP, rede diferente).
    ///
    /// Requisitos:
    ///   - Android 8.0 (API 26) ou superior para Local-Only Hotspot.
    ///   - Permissão ACCESS_WIFI_STATE e CHANGE_WIFI_STATE no AndroidManifest.xml.
    ///   - HotspotManager.java compilado em Assets/Plugins/Android/.
    /// </summary>
    public class HotspotFallbackManager : MonoBehaviour
    {
        [Header("Configuração")]
        [Tooltip("Segundos sem encontrar headset antes de ativar o hotspot.")]
        public float timeoutSeconds = 30f;

        [Header("Referências")]
        [Tooltip("Necessário para verificar se um headset foi encontrado.")]
        public WifiDiscoveryManager discoveryManager;

        [Tooltip("Opcional — informa o usuário via voz sobre o estado do hotspot.")]
        public VoiceDirectorService voiceDirector;

        // Estado público para consulta por outros sistemas
        public bool IsHotspotActive { get; private set; }

        private float _timer;
        private bool _isAndroid;

        // ─── Ciclo de Vida ────────────────────────────────────────────────

        private void Start()
        {
            _isAndroid = Application.platform == RuntimePlatform.Android;

            if (discoveryManager != null)
                discoveryManager.OnHeadsetFound += OnHeadsetDiscovered;
        }

        private void Update()
        {
            // Não faz nada se o hotspot já está ativo ou se estamos em modo de IP fixo
            if (IsHotspotActive) return;
            if (discoveryManager != null && discoveryManager.usarIpFixo) return;

            // Não conta o tempo se um headset já foi encontrado
            if (discoveryManager != null && discoveryManager.detectedHeadsets.Count > 0)
            {
                _timer = 0f;
                return;
            }

            _timer += Time.deltaTime;

            if (_timer >= timeoutSeconds)
            {
                _timer = 0f;
                TryActivateHotspot();
            }
        }

        private void OnDestroy()
        {
            if (discoveryManager != null)
                discoveryManager.OnHeadsetFound -= OnHeadsetDiscovered;

            if (IsHotspotActive)
                DeactivateHotspot();
        }

        // ─── Ativação do Hotspot ──────────────────────────────────────────

        private void TryActivateHotspot()
        {
            Debug.Log("[HotspotFallback] Timeout de descoberta Wi-Fi. Tentando ativar hotspot...");

            voiceDirector?.Enqueue(
                "Nenhum dispositivo encontrado na rede. Ativando ponto de acesso do celular.",
                VoicePriority.System);

#if UNITY_ANDROID
            if (_isAndroid)
            {
                ActivateAndroidHotspot();
                return;
            }
#endif
            // Editor: simula ativação
            Debug.Log("[HotspotFallback] EDITOR: Hotspot simulado ativado.");
            IsHotspotActive = true;
        }

#if UNITY_ANDROID
        private void ActivateAndroidHotspot()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // Obtém o singleton HotspotManager do Java
                    var hotspotClass = new AndroidJavaClass("com.luckarkman.xr.HotspotManager");
                    using (var manager = hotspotClass.CallStatic<AndroidJavaObject>("getInstance"))
                    {
                        // Passa a Activity e o nome do GameObject para receber os callbacks
                        manager.Call("startHotspot", activity, gameObject.name);
                        Debug.Log("[HotspotFallback] startHotspot() chamado.");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[HotspotFallback] Falha ao chamar HotspotManager: {e.Message}");
                voiceDirector?.Enqueue(
                    "Não foi possível ativar o ponto de acesso. Verifique as permissões.",
                    VoicePriority.System);
            }
        }
#endif

        private void DeactivateHotspot()
        {
            IsHotspotActive = false;

#if UNITY_ANDROID
            if (!_isAndroid) return;
            try
            {
                var hotspotClass = new AndroidJavaClass("com.luckarkman.xr.HotspotManager");
                using (var manager = hotspotClass.CallStatic<AndroidJavaObject>("getInstance"))
                {
                    manager.Call("stopHotspot");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HotspotFallback] Erro ao desativar hotspot: {e.Message}");
            }
#endif
        }

        // ─── Callbacks via UnitySendMessage (Java → C#) ───────────────────

        /// <summary>Chamado pelo HotspotManager.java quando o hotspot é iniciado.</summary>
        public void OnHotspotStarted(string ssidInfo)
        {
            IsHotspotActive = true;
            Debug.Log($"[HotspotFallback] Hotspot ativo. SSID info: {ssidInfo}");

            voiceDirector?.Enqueue(
                "Ponto de acesso ativado! Conecte o óculos ao Wi-Fi do celular e aguarde.",
                VoicePriority.System);
        }

        /// <summary>Chamado pelo HotspotManager.java quando o hotspot é parado.</summary>
        public void OnHotspotStopped(string _)
        {
            IsHotspotActive = false;
            Debug.Log("[HotspotFallback] Hotspot encerrado.");
        }

        /// <summary>Chamado pelo HotspotManager.java quando ocorre um erro.</summary>
        public void OnHotspotFailed(string reason)
        {
            IsHotspotActive = false;

            string msg = reason switch
            {
                "API_TOO_LOW"   => "Versão do Android incompatível com hotspot automático.",
                "1"             => "Hotspot falhou: nenhum canal de rádio disponível.",
                "2"             => "Hotspot falhou: erro genérico do sistema.",
                "3"             => "Hotspot falhou: conflito com modo atual do Wi-Fi.",
                "4"             => "Hotspot bloqueado pelo sistema operacional.",
                _               => $"Hotspot falhou: {reason}"
            };

            Debug.LogWarning($"[HotspotFallback] {msg}");
            voiceDirector?.Enqueue(msg, VoicePriority.System);
        }

        // ─── Callback do WifiDiscovery ────────────────────────────────────

        private void OnHeadsetDiscovered()
        {
            // Headset encontrado (seja via Wi-Fi local ou via hotspot) — reseta o timer
            _timer = 0f;
            Debug.Log("[HotspotFallback] Headset encontrado. Timer resetado.");
        }
    }
}
