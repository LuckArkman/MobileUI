using UnityEngine;
using System;
using LuckArkman.XR.Voice;

namespace LuckArkman.XR.Navigation
{
    /// <summary>
    /// Integração com o Google Maps para navegação turn-by-turn.
    ///
    /// Estratégia MVP (pragmática e sem dependência de SDK externo):
    ///   1. Aceita o destino em texto digitado pelo usuário.
    ///   2. Lança o aplicativo Google Maps via Intent Android / deep link universal
    ///      usando Application.OpenURL() — API nativa Unity.
    ///   3. O app Google Maps assume o áudio de navegação turn-by-turn.
    ///   4. NOSSO app continua em background (BackgroundServiceManager garante isso)
    ///      executando o pipeline MiDaS/YOLO de detecção de obstáculos.
    ///   5. Os alertas de obstáculos do SmallPrinceTTS são sobrepostos ao Maps.
    ///
    /// Por que esta abordagem?
    ///   O Navigation SDK for Android exige configuração de projeto Android nativo
    ///   (Gradle, licença paga de uso, fragmento de UI), o que não é compatível com
    ///   o pipeline de build Unity sem um plugin nativo completo. O deep link oferece
    ///   toda a capacidade de navegação do Maps sem essas dependências.
    ///
    /// Evolução futura:
    ///   Migrar para NavBridgePlugin.java + Navigation SDK quando o build Android
    ///   nativo for separado e a licença for configurada.
    /// </summary>
    public class GoogleMapsNavigator : MonoBehaviour
    {
        public static GoogleMapsNavigator Instance { get; private set; }

        [Header("Referências")]
        [Tooltip("Opcional. Informa o usuário via voz sobre estado da navegação.")]
        public VoiceDirectorService voiceDirector;

        [Header("Configuração")]
        [Tooltip("Modo de transporte: w=caminhando, d=dirigindo, b=bicicleta.")]
        public string travelMode = "w";

        /// <summary>True se uma rota está atualmente ativa.</summary>
        public bool IsNavigating { get; private set; }

        /// <summary>Destino atual em texto (para exibição na UI).</summary>
        public string CurrentDestination { get; private set; }

        // ─── Ciclo de Vida ────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ─── API Pública ──────────────────────────────────────────────────

        /// <summary>
        /// Inicia a navegação para o destino informado.
        /// Lança o Google Maps com o destino e ativa o modo de navegação.
        /// </summary>
        /// <param name="destination">Endereço ou nome do local de destino.</param>
        public void StartNavigation(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                Debug.LogWarning("[GoogleMaps] Destino vazio. Navegação não iniciada.");
                voiceDirector?.Enqueue("Por favor, informe um destino válido.", VoicePriority.System);
                return;
            }

            CurrentDestination = destination.Trim();
            IsNavigating = true;

            // Codifica o destino para uso em URL (espaços → %20, etc.)
            string encodedDestination = Uri.EscapeDataString(CurrentDestination);

            // Esquema de URI do Google Maps para navegação:
            // google.navigation:q=DESTINO&mode=MODO
            // Funciona no Android se o Google Maps estiver instalado.
            // Fallback: maps.google.com para outros browsers/sistemas.
            string mapsUri = $"google.navigation:q={encodedDestination}&mode={travelMode}";

            Debug.Log($"[GoogleMaps] Abrindo navegação para: '{CurrentDestination}' | URI: {mapsUri}");

            // Application.OpenURL() lança a intent no Android e abre o browser no Editor.
            Application.OpenURL(mapsUri);

            // Informa o usuário via TTS
            voiceDirector?.Enqueue(
                $"Rota iniciada para {CurrentDestination}. Siga as instruções do mapa.",
                VoicePriority.Navigation);
        }

        /// <summary>
        /// Encerra o modo de navegação.
        /// Não fecha o Google Maps (o usuário pode querer continuar usando o mapa).
        /// </summary>
        public void StopNavigation()
        {
            if (!IsNavigating) return;

            string previous = CurrentDestination;
            IsNavigating = false;
            CurrentDestination = string.Empty;

            Debug.Log($"[GoogleMaps] Navegação para '{previous}' encerrada.");
            voiceDirector?.Enqueue("Navegação encerrada.", VoicePriority.System);
        }
    }
}
