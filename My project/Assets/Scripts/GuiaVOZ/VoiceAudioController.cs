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
        [Range(0f, 3f)] [Tooltip("Graves (Bass) - Corpo e profundidade da voz, reduz se estiver soando 'abafado'.")]
        public float graves = 1.0f;

        [Range(0f, 3f)] [Tooltip("Médios (Mid) - Clareza vocal para ser compreendido durante os passos e barulhos da rua.")]
        public float medios = 1.0f;

        [Range(0f, 3f)] [Tooltip("Agudos (Treble) - Sibilância e ar na voz, suavize se o 'S' estiver chiando.")]
        public float agudos = 1.0f;

        private AudioSource _audioSource;

        // Frequências clássicas para cruzamento paramétrico vocal
        private float crossoverGravesMedios = 400.0f; 
        private float crossoverMediosAgudos = 4000.0f; 
        
        // Memória de estado de sinal digital (para separação Estéreo L/R)
        private float[] estadoGrave = new float[2];
        private float[] estadoMedio = new float[2];
        
        void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            AplicarParametrosPrimarios();
        }

        void Update()
        {
            // Aplica os parâmetros nativos contínua e suavemente
            AplicarParametrosPrimarios();
        }

        private void AplicarParametrosPrimarios()
        {
            if (_audioSource != null)
            {
                _audioSource.volume = volumeMaster;
                _audioSource.pitch = tomVelocidade;
            }
        }

        /// <summary>
        /// Hack DSP para Interceptar a onda de aúdio Raw da Unity, e emulando 
        /// um verdadeiro circuito de Equalização Analógico em tempo de CPU!
        /// Assim o usuário ajusta no visual do Inspector os valores numéricos.
        /// </summary>
        void OnAudioFilterRead(float[] data, int channels)
        {
            float limitRate = AudioSettings.outputSampleRate;
            if (limitRate == 0f) return;

            // Fator Pi suavizado correspondente ao Shelving de EQ paramétrico clássico de som
            float fatorGrave = 2.0f * Mathf.Sin(Mathf.PI * crossoverGravesMedios / limitRate);
            float fatorAgudo = 2.0f * Mathf.Sin(Mathf.PI * crossoverMediosAgudos / limitRate);

            // Cuidado: Emulação IIR (Infinite Impulse Response) DSP no Buffer Principal de áudio nativo 
            for (int i = 0; i < data.Length; i += channels)
            {
                // Varre independentemente (Mono: 1 canal | Stereo: 2 canais - Esquerdo/Direito pan)
                for (int c = 0; c < channels; c++)
                {
                    if (c > 1) continue; // Prevenção de corrupção se Unity despachar Surround 5.1/7.1

                    int iCanalIndex = i + c;
                    float frameSampleOriginal = data[iCanalIndex];

                    // Divisor em cascata (Rede Crossover de Estado)
                    estadoGrave[c] = estadoGrave[c] + fatorGrave * (frameSampleOriginal - estadoGrave[c]);
                    estadoMedio[c] = estadoMedio[c] + fatorAgudo * (estadoGrave[c] - estadoMedio[c]);

                    // Isolamento e cálculo das três camadas absolutas
                    float ondaGrave  = estadoMedio[c];
                    float ondaMedia  = estadoGrave[c] - estadoMedio[c];
                    float ondaAguda  = frameSampleOriginal - estadoGrave[c];

                    // O som mesclado retorna ao ouvinte (Mixagem da Amp DSP)
                    float resultadoFinalMix = (ondaGrave * graves) + (ondaMedia * medios) + (ondaAguda * agudos);

                    data[iCanalIndex] = resultadoFinalMix;
                }
            }
        }
    }
}
