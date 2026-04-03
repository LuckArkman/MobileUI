using UnityEngine;
using System.Collections.Generic;
using LuckArkman.XR.Networking; // Necessário para UnityMainThreadDispatcher
#if UNITY_ANDROID
using System;
#endif

namespace LuckArkman.XR.Voice
{
    /// <summary>
    /// Motor TTS temático "O Pequeno Príncipe".
    /// Encapsula android.speech.tts.TextToSpeech com pitch/rate ajustados
    /// para simular voz masculina infantil e suave.
    /// No Editor (não-Android), as falas são impressas no Console.
    /// </summary>
    public class SmallPrinceTTS : MonoBehaviour
    {
        [Header("Configuração da Voz")]
        [Tooltip("Pitch > 1.0 = mais agudo. Recomendado: 1.25–1.40 para voz infantil.")]
        [Range(0.5f, 2.0f)] public float voicePitch = 1.3f;

        [Tooltip("Taxa de fala. < 1.0 = mais lento/pausado. Recomendado: 0.85–0.95.")]
        [Range(0.5f, 2.0f)] public float voiceRate = 0.9f;

        /// <summary>True enquanto o Android TTS está reproduzindo áudio.</summary>
        public bool IsSpeaking { get; private set; }

        // Mapeamento de palavras-chave para frases temáticas.
        // Busca por substring (case-insensitive) na mensagem recebida.
        private static readonly Dictionary<string, string> s_themedPhrases =
            new Dictionary<string, string>
            {
                { "pare",             "Atenção! Pare imediatamente, pequeno príncipe!" },
                { "parar",            "Pare! Há perigo à sua frente!" },
                { "girar esquerda",   "Gire para a esquerda agora, com cuidado!" },
                { "girar direita",    "Gire para a direita, como a estrela que te guia!" },
                { "desviar esquerda", "Desvie para a esquerda!" },
                { "desviar direita",  "Desvie para a direita, como a rosa que precisa de ar!" },
                { "livre",            "O caminho está aberto! Siga em frente com confiança!" },
                { "continue",         "Continue! O horizonte está lindo hoje." },
                { "cuidado",          "Vá com cuidado, pequeno príncipe." },
                { "atenção",          "Atenção à frente, mas pode avançar devagar." },
                { "chegou",           "Parabéns! Você chegou. O Pequeno Príncipe está orgulhoso!" },
                { "rota iniciada",    "Rota iniciada! Siga as minhas instruções, amigo." },
            };

#if UNITY_ANDROID
        private AndroidJavaObject _ttsEngine;
        // Referência mantida em campo para evitar que o GC colete o proxy
        // antes do Android terminar de usar o listener.
        private TtsInitListener _initListener;
        private bool _isReady;
#endif

        // ─── Ciclo de Vida ────────────────────────────────────────────────

        private void Start()
        {
#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android)
                InitializeTts();
#endif
        }

        private void Update()
        {
            // Atualiza IsSpeaking consultando a API Android a cada frame.
            // Evita callback assíncrono e simplifica o controle de fila.
#if UNITY_ANDROID
            if (_ttsEngine != null && _isReady)
            {
                try { IsSpeaking = _ttsEngine.Call<bool>("isSpeaking"); }
                catch { IsSpeaking = false; }
            }
#endif
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID
            if (_ttsEngine != null)
            {
                try { _ttsEngine.Call("shutdown"); }
                catch { /* ignora ao destruir */ }
                finally { _ttsEngine.Dispose(); _ttsEngine = null; }
            }
#endif
        }

        // ─── Inicialização Android ────────────────────────────────────────

#if UNITY_ANDROID
        private void InitializeTts()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _initListener = new TtsInitListener(this);
                    // Construtor: TextToSpeech(Context context, OnInitListener listener)
                    _ttsEngine = new AndroidJavaObject(
                        "android.speech.tts.TextToSpeech", activity, _initListener);
                    Debug.Log("[SmallPrinceTTS] TextToSpeech inicializando...");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SmallPrinceTTS] Falha ao criar TTS: {e.Message}");
            }
        }

        /// <summary>
        /// Chamado pelo TtsInitListener (thread Android) via UnityMainThreadDispatcher.
        /// </summary>
        public void OnTtsReady()
        {
            if (_ttsEngine == null) return;
            try
            {
                // Locale pt-BR: usa construtor Locale(String language, String country)
                using (var locale = new AndroidJavaObject("java.util.Locale", "pt", "BR"))
                {
                    // setLanguage retorna int (SUCCESS=0, MISSING_DATA=-2, NOT_SUPPORTED=-1)
                    int result = _ttsEngine.Call<int>("setLanguage", locale);
                    if (result < 0)
                        Debug.LogWarning($"[SmallPrinceTTS] Locale pt-BR não suportado: {result}. Usando padrão do sistema.");
                }

                // setSpeechRate e setPitch retornam int (ignorado)
                _ttsEngine.Call<int>("setSpeechRate", voiceRate);
                _ttsEngine.Call<int>("setPitch", voicePitch);

                _isReady = true;
                Debug.Log($"[SmallPrinceTTS] Pronto. pitch={voicePitch}, rate={voiceRate}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SmallPrinceTTS] Falha na configuração do locale: {e.Message}");
            }
        }
#endif

        // ─── API Pública ──────────────────────────────────────────────────

        /// <summary>Fala o texto. Enfileira se o TTS estiver ocupado.</summary>
        public void Speak(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string phrase = ApplyTheme(text);

#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android)
            {
                SpeakInternal(phrase, flush: false);
                return;
            }
#endif
            Debug.Log($"[TTS] 🎙️ «{phrase}»");
        }

        /// <summary>Interrompe a fala atual e fala imediatamente (para obstáculos).</summary>
        public void InterruptAndSpeak(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string phrase = ApplyTheme(text);

#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android)
            {
                SpeakInternal(phrase, flush: true);
                return;
            }
#endif
            Debug.Log($"[TTS URGENTE] 🚨 «{phrase}»");
        }

        // ─── Internos ─────────────────────────────────────────────────────

#if UNITY_ANDROID
        private void SpeakInternal(string text, bool flush)
        {
            if (_ttsEngine == null || !_isReady) return;
            try
            {
                // speak(CharSequence text, int queueMode, Bundle params, String utteranceId)
                // QUEUE_FLUSH = 0, QUEUE_ADD = 1
                // Bundle é null (parâmetros opcionais não necessários aqui)
                int queueMode = flush ? 0 : 1;
                _ttsEngine.Call<int>("speak", text, queueMode, null, "midas_utt");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SmallPrinceTTS] Erro ao falar: {e.Message}");
            }
        }
#endif

        private string ApplyTheme(string input)
        {
            string lower = input.ToLower();
            foreach (var pair in s_themedPhrases)
            {
                if (lower.Contains(pair.Key))
                    return pair.Value;
            }
            return input; // retorna original se não houver mapeamento
        }

        // ─── Proxy Java ───────────────────────────────────────────────────

#if UNITY_ANDROID
        /// <summary>
        /// Implementa android.speech.tts.TextToSpeech$OnInitListener via AndroidJavaProxy.
        /// AndroidJavaProxy permite implementar interfaces Java em C#.
        /// </summary>
        private sealed class TtsInitListener : AndroidJavaProxy
        {
            private readonly SmallPrinceTTS _owner;

            public TtsInitListener(SmallPrinceTTS owner)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _owner = owner;
            }

            // Chamado pelo Android quando o TTS termina de inicializar.
            // status == 0 → TextToSpeech.SUCCESS
            // Atenção: chamado de uma thread Android, não da Unity Main Thread.
            public void onInit(int status)
            {
                if (status == 0)
                {
                    // Despacha para a Unity Main Thread antes de tocar em qualquer API Unity.
                    UnityMainThreadDispatcher.Instance()
                        .Enqueue(() => _owner.OnTtsReady());
                }
                else
                {
                    UnityEngine.Debug.LogError(
                        $"[SmallPrinceTTS] Falha na inicialização do TTS. Status: {status}");
                }
            }
        }
#endif
    }
}
