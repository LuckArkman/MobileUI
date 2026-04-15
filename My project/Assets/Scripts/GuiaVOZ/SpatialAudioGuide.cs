using UnityEngine;

namespace LuckArkman.XR.Main
{
    /// <summary>
    /// Dono exclusivo do AudioSource de voz.
    /// Todo áudio — seja Piper TTS, Fallback ou qualquer outra fonte —
    /// DEVE passar por esta classe. Nenhum externo acessa o AudioSource diretamente.
    /// </summary>
    public class SpatialAudioGuide : MonoBehaviour
    {
        [Header("Configuração de Áudio 3D")]
        [Tooltip("O AudioSource que vai tocar a voz da IA")]
        [SerializeField] private AudioSource voiceAudioSource;

        [Tooltip("Força do direcionamento do som (0 = Centro, 1 = Totalmente de um lado)")]
        [Range(0f, 1f)] public float intensidadeDoPan = 0.8f;

        // Referência ao clip dinâmico atual (gerado pelo Piper).
        // Gerenciado aqui para garantir Destroy correto antes de domain reload.
        private AudioClip _clipPiperAtual = null;

        // FIX: Inicializa o AudioSource no Awake (antes do Start de qualquer outro script)
        // Garante que o AudioSource esteja pronto mesmo se outro script chamar
        // ReproduziirClipPiper() logo no primeiro frame.
        private void Awake()
        {
            if (voiceAudioSource == null)
                voiceAudioSource = GetComponent<AudioSource>();

            if (voiceAudioSource == null)
            {
                // AudioSource não existia: cria um novo com defaults seguros.
                // Só neste caso aplicamos valores padrão — não sobrescrevemos
                // um AudioSource que o utilizador já configurou no Inspector.
                voiceAudioSource = gameObject.AddComponent<AudioSource>();
                voiceAudioSource.spatialBlend = 0f;
                voiceAudioSource.playOnAwake  = false;
                voiceAudioSource.loop         = false;
                Debug.LogWarning("[SpatialAudio] AudioSource não encontrado no Inspector — criado automaticamente.");
            }
            // Se o AudioSource já existia: respeitamos TODOS os parâmetros
            // que o utilizador definiu no Inspector (volume, pitch, spatialBlend, etc.)
        }

        private void Start()
        {
            if (voiceAudioSource == null)
                voiceAudioSource = GetComponent<AudioSource>();

            // Acopla o equalizador DSP apenas se ainda não estiver presente.
            // Não sobrescrevemos nenhum parâmetro do AudioSource aqui.
            if (voiceAudioSource != null &&
                voiceAudioSource.gameObject.GetComponent<VoiceAudioController>() == null)
            {
                voiceAudioSource.gameObject.AddComponent<VoiceAudioController>();
                Debug.Log("[SpatialAudio] Equalizador DSP anexado ao AudioSource.");
            }
        }

        // -----------------------------------------------------------------------
        // API PÚBLICA — Áudio gerado pelo Piper ONNX TTS
        // -----------------------------------------------------------------------

        /// <summary>
        /// Reproduz um AudioClip gerado dinamicamente pelo Piper TTS.
        /// Ajusta o pan estéreo conforme o comando de navegação antes do Play.
        /// Destrói o clip anterior para evitar vazamento de memória nativa.
        /// </summary>
        public bool ReproduziirClipPiper(AudioClip clip, Guia.EstadoInstrucao comando)
        {
            // Diagnóstico completo — visível no Console e no logcat Android
            if (voiceAudioSource == null)
            {
                Debug.LogError("[SpatialAudio] FALHA: voiceAudioSource é NULL. Arraste um AudioSource para o campo no Inspector.");
                return false;
            }
            if (clip == null)
            {
                Debug.LogError("[SpatialAudio] FALHA: AudioClip recebido é NULL.");
                return false;
            }
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError($"[SpatialAudio] FALHA: Clip ainda não carregado (estado: {clip.loadState}).");
                return false;
            }
            if (clip.length <= 0f)
            {
                Debug.LogError("[SpatialAudio] FALHA: Clip com duração zero.");
                return false;
            }

            // Para reprodução anterior
            if (voiceAudioSource.isPlaying)
                voiceAudioSource.Stop();

            // Destrói o clip Piper anterior (libera memória nativa)
            if (_clipPiperAtual != null)
            {
                Destroy(_clipPiperAtual);
                _clipPiperAtual = null;
            }

            // Aplica direção espacial estereô
            AjustarDirecaoDoSom(comando);

            _clipPiperAtual       = clip;
            voiceAudioSource.clip = _clipPiperAtual;
            voiceAudioSource.Play();

            Debug.Log($"[SpatialAudio] ✓ PLAY | '{comando}' | Pan: {voiceAudioSource.panStereo:+0.0;-0.0;0} | Duração: {clip.length:F2}s | AudioSource.isPlaying: {voiceAudioSource.isPlaying}");
            return true;
        }

        // -----------------------------------------------------------------------
        // API PÚBLICA — Clipes de Fallback (AudioClips estáticos do projeto)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Reproduz um AudioClip estático de fallback (asset do projeto).
        /// Não destrói clipes estáticos — eles são geenciados pelo AssetDatabase.
        /// </summary>
        public bool ReproduziirClipFallback(AudioClip clip, Guia.EstadoInstrucao comando)
        {
            if (voiceAudioSource == null || clip == null || voiceAudioSource.isPlaying)
                return false;

            AjustarDirecaoDoSom(comando);
            voiceAudioSource.clip = clip;
            voiceAudioSource.Play();
            return true;
        }

        // -----------------------------------------------------------------------
        // API PÚBLICA — Utiliários de estado
        // -----------------------------------------------------------------------

        public bool EstaReproduziindo => voiceAudioSource != null && voiceAudioSource.isPlaying;

        public void PararAudio()
        {
            if (voiceAudioSource != null && voiceAudioSource.isPlaying)
                voiceAudioSource.Stop();
        }

        // -----------------------------------------------------------------------
        // DIREÇÃO ESTEREÔ (interno — chamado antes de qualquer Play)
        // -----------------------------------------------------------------------

        public void AjustarDirecaoDoSom(Guia.EstadoInstrucao comando)
        {
            if (voiceAudioSource == null) return;

            switch (comando)
            {
                case Guia.EstadoInstrucao.DesviarDireita:
                case Guia.EstadoInstrucao.GirarDireita:
                case Guia.EstadoInstrucao.DesviarDuploDireita:
                    voiceAudioSource.panStereo = intensidadeDoPan;
                    break;

                case Guia.EstadoInstrucao.DesviarEsquerda:
                case Guia.EstadoInstrucao.GirarEsquerda:
                case Guia.EstadoInstrucao.DesviarDuploEsquerda:
                    voiceAudioSource.panStereo = -intensidadeDoPan;
                    break;

                default: // Parar, Frente*, Nenhum — centralizado
                    voiceAudioSource.panStereo = 0f;
                    break;
            }
        }

        // -----------------------------------------------------------------------
        // CICLO DE VIDA — cleanup do clip dinâmico antes do domain reload
        // -----------------------------------------------------------------------

        private void OnDestroy()
        {
            if (_clipPiperAtual != null)
            {
                Destroy(_clipPiperAtual);
                _clipPiperAtual = null;
            }
        }
    }
}