using UnityEngine;

namespace LuckArkman.XR.Main
{
    [RequireComponent(typeof(AudioSource))]
    public class VoiceAudioController : MonoBehaviour
    {
        [Header("=====================================")]
        [Header(" CONTROLE E QUALIDADE DA VOZ GUIA")]
        [Header("=====================================")]

        [Header("Intensidade Geral")]
        [Range(0f, 2f)] [Tooltip("Volume Mestre da Voz do Pipeline TTS.")]
        public float volumeMaster = 1.0f;

        [Range(0.5f, 1.5f)] [Tooltip("Afinação (Pitch): Deixa a voz mais grave/lenta ou mais fina/rápida.")]
        public float tomVelocidade = 1.0f;

        [Header("Equalização 3-Bandas Paramétrica (DSP)")]
        [Range(0f, 3f)] [Tooltip("Graves (Bass) - Corpo e profundidade da voz.")]
        public float graves = 1.0f;

        [Range(0f, 3f)] [Tooltip("Médios (Mid) - Clareza vocal para ser compreendido em ambientes urbanos.")]
        public float medios = 1.0f;

        [Range(0f, 3f)] [Tooltip("Agudos (Treble) - Sibilância e ar na voz.")]
        public float agudos = 1.0f;

        private AudioSource _audioSource;

        // Frequências de cruzamento paramétrico vocal
        private const float CrossoverGravesMedios = 400f;
        private const float CrossoverMediosAgudos = 4000f;

        // FIX ERRO 1: Taxa de amostragem cacheada na Main Thread (Awake).
        // OnAudioFilterRead roda na thread de áudio (DSP thread) — AudioSettings
        // não pode ser acessado lá. Este int é lido de forma thread-safe.
        private int _cachedSampleRate;

        // Estado IIR por canal (Stereo: índice 0=Esquerdo, 1=Direito)
        private float[] _estadoGrave = new float[2];
        private float[] _estadoMedio = new float[2];

        void Awake()
        {
            _audioSource = GetComponent<AudioSource>();

            // Captura o sample rate aqui, na Main Thread, antes de qualquer
            // chamada ao callback OnAudioFilterRead.
            _cachedSampleRate = AudioSettings.outputSampleRate;

            AplicarParametrosPrimarios();
        }

        void Update()
        {
            AplicarParametrosPrimarios();
        }

        private void AplicarParametrosPrimarios()
        {
            if (_audioSource != null)
            {
                _audioSource.volume = volumeMaster;
                _audioSource.pitch  = tomVelocidade;
            }
        }

        /// <summary>
        /// Callback de DSP — roda na thread de áudio, não na Main Thread.
        /// Acessa APENAS campos de instância e constantes; nunca APIs Unity.
        /// </summary>
        void OnAudioFilterRead(float[] data, int channels)
        {
            // Usa o valor cacheado em Awake — thread-safe.
            if (_cachedSampleRate == 0) return;

            float limitRate = (float)_cachedSampleRate;
            float fatorGrave = 2.0f * Mathf.Sin(Mathf.PI * CrossoverGravesMedios / limitRate);
            float fatorAgudo = 2.0f * Mathf.Sin(Mathf.PI * CrossoverMediosAgudos / limitRate);

            for (int i = 0; i < data.Length; i += channels)
            {
                for (int c = 0; c < channels; c++)
                {
                    if (c > 1) continue; // Previne acesso fora do array em Surround (5.1/7.1)

                    int idx = i + c;
                    float sample = data[idx];

                    _estadoGrave[c] = _estadoGrave[c] + fatorGrave * (sample        - _estadoGrave[c]);
                    _estadoMedio[c] = _estadoMedio[c] + fatorAgudo * (_estadoGrave[c] - _estadoMedio[c]);

                    float ondaGrave = _estadoMedio[c];
                    float ondaMedia = _estadoGrave[c] - _estadoMedio[c];
                    float ondaAguda = sample - _estadoGrave[c];

                    data[idx] = (ondaGrave * graves) + (ondaMedia * medios) + (ondaAguda * agudos);
                }
            }
        }

        // FIX ERRO 3 (parte VoiceAudioController): Limpa referência ao AudioSource
        // e zera os estados do filtro IIR para evitar que handles inválidos sejam
        // liberados durante domain reload.
        private void OnDestroy()
        {
            _audioSource = null;
            _estadoGrave[0] = _estadoGrave[1] = 0f;
            _estadoMedio[0] = _estadoMedio[1] = 0f;
        }
    }
}
