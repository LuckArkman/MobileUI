using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using System.Text;
using System.Collections;

namespace LuckArkman.XR.Main
{
    public class Guia : MonoBehaviour
    {
        public enum EstadoInstrucao { Nenhum, Parar, DesviarDireita, DesviarEsquerda, GirarDireita, GirarEsquerda, DesviarDuploDireita, DesviarDuploEsquerda, Frente1, Frente2, Frente3, Frente4 }

        [Header("Módulos Integrados")]
        public SpatialAudioGuide spatialAudio;

        [Tooltip("Motor TTS ONNX on-device (sem rede). Arraste o GameObject com PiperOnnxTTS aqui.")]
        public PiperOnnxTTS piperOnnxTTS;

        [Header("Generative AI: LLaMA (Ollama Docker Notebook)")]
        [Tooltip("Ative para usar o modelo LLaMA local no seu notebook conectado no roteador do celular.")]
        public bool usarLlamaText = false;
        public string llamaEndpointUrl = "http://192.168.43.10:11434/api/generate";
        public string llamaModelName = "llama3";

        [Header("Generative AI: Piper ONNX TTS (Voz Infantil Local)")]
        [Tooltip("Ative para usar o Piper ONNX para converter os comandos de texto do LLaMA ou os hardcoded em áudio (com a temática menino, em espanhol).")]
        public bool usarPiperLocal = true;
        public string piperEndpointUrl = "http://192.168.43.10:5000/";

        [Header("Sistema de Voz Guia (Fallback Físico)")]
        public AudioSource voiceAudioSource;
        
        public AudioClip voiceMoveLeft;
        public AudioClip voiceMoveRight;
        public AudioClip voiceTurnLeft;
        public AudioClip voiceTurnRight;
        public AudioClip voiceStop;
        
        [Header("Vozes de Evasão Complexa")]
        public AudioClip voiceMoveDoubleLeft;  
        public AudioClip voiceMoveDoubleRight; 

        [Header("Vozes de Progressão Frontal")]
        public AudioClip voiceFrente1;
        public AudioClip voiceFrente2; 
        public AudioClip voiceFrente3; 
        public AudioClip voiceFrente4; 

        [Header("Tempos Dinâmicos de Espera (Segundos)")]
        public float tempoEsperaParar = 1.0f;     
        public float tempoEsperaAcao = 2.0f;      
        public float tempoEsperaContinuar = 3.5f; 
        
        private float proximoTempoDeFala = 0f;
        private EstadoInstrucao instrucaoAnterior = EstadoInstrucao.Nenhum;

        // FIX ERRO 2: Guard que impede múltiplas corrotinas de TTS paralelas.
        private Coroutine _coroutineTTSAtiva = null;

        // Estrutura para Ollama (LLaMA via Docker)
        [System.Serializable] private class OllamaRequest { public string model; public string prompt; public bool stream; }
        [System.Serializable] private class OllamaResponse { public string response; public bool done; }

        private void Start()
        {
            if (voiceAudioSource == null)
            {
                voiceAudioSource = gameObject.AddComponent<AudioSource>();
                voiceAudioSource.playOnAwake = false;
            }

            // ── Auto-detecção do motor TTS on-device ─────────────────────────────────
            // Se o campo não foi preenchido no Inspector, procura na cena automaticamente.
            if (piperOnnxTTS == null)
                piperOnnxTTS = FindObjectOfType<PiperOnnxTTS>();

            if (piperOnnxTTS != null)
            {
                // Motor on-device encontrado: desativa o caminho HTTP para evitar
                // pings de timeout desnecessários ao servidor Python externo.
                usarPiperLocal = false;
                Debug.Log("[Guia ✅] PiperOnnxTTS on-device detectado. TTS offline activado, servidor HTTP desativado.");
            }
            else if (usarPiperLocal)
            {
                // Nenhum motor ONNX disponível: verifica se o servidor HTTP está online.
                StartCoroutine(VerificarConectividadePiper());
            }
            else
            {
                Debug.LogWarning("[Guia] Nenhum motor TTS configurado. Apenas AudioClips de fallback serão usados.");
            }
        }

        /// <summary>
        /// Faz um ping leve ao servidor Piper assim que o app inicia.
        /// Se o servidor não responder em 2s, desativa o Piper e avisa no Console.
        /// Evita 8 segundos de timeout em cada comando de navegação.
        /// </summary>
        private IEnumerator VerificarConectividadePiper()
        {
            string pingUrl = $"{piperEndpointUrl.TrimEnd('/')}/?text=test";

            using (UnityWebRequest ping = UnityWebRequest.Head(pingUrl))
            {
                ping.timeout = 2;
                yield return ping.SendWebRequest();

                if (ping.result == UnityWebRequest.Result.Success ||
                    ping.responseCode == 400) // 400 = servidor respondeu (texto vazio), mas está online
                {
                    Debug.Log($"[Piper ✅ SERVIDOR ONLINE] Conectado em '{piperEndpointUrl}'");
                }
                else
                {
                    usarPiperLocal = false;
                    Debug.LogError(
                        $"[Piper ❌ SERVIDOR OFFLINE]\n" +
                        $"  Não foi possível conectar em '{piperEndpointUrl}'\n" +
                        $"  Erro: {ping.error}\n" +
                        $"  \u25ba Ação necessária: Execute 'Iniciar_Piper_Server.bat' no notebook\n" +
                        $"    Caminho: Assets/Scripts/GuiaVOZ/Piper_ONNX_Model/Iniciar_Piper_Server.bat\n" +
                        $"  O sistema usará os clipes de voz locais como fallback nesta sessão."
                    );
                }
            }
        }

        public void ExecutarComando(EstadoInstrucao comandoDecidido, int passosObjeto = 0, string descricaoAmbiente = "")
        {
            // Não interrompe áudio que ainda está a tocar
            if (spatialAudio != null && spatialAudio.EstaReproduziindo) return;

            if (Time.time >= proximoTempoDeFala)
            {
                if (comandoDecidido != instrucaoAnterior)
                {
                    TocarComandoDeVoz(comandoDecidido, passosObjeto, descricaoAmbiente);
                    instrucaoAnterior = comandoDecidido;
                }
            }
        }

        /// <summary>
        /// Entry point cuando el MiDaS tiene un resultado completo.
        /// Genera automáticamente la descripción en español y la envía al Piper.
        /// </summary>
        public void ExecutarComandoComMidas(EstadoInstrucao comandoDecidido,
                                            LuckArkman.XR.AI.MidasResult midasResult,
                                            int passosObjeto = 0)
        {
            // Não interrompe áudio que ainda está a tocar
            if (spatialAudio != null && spatialAudio.EstaReproduziindo) return;

            if (Time.time >= proximoTempoDeFala)
            {
                string descricaoMidas = midasResult.GerarDescricaoEspanhol();

                Debug.Log(
                    $"[MiDaS → Piper TTS]\n" +
                    $"  Comando    : {comandoDecidido}\n" +
                    $"  Frente     : {midasResult.dangerScore:F1}/10\n" +
                    $"  Esquerda   : {midasResult.leftZoneDanger:F1}/10\n" +
                    $"  Direita    : {midasResult.rightZoneDanger:F1}/10\n" +
                    $"  Mov.Rápido : {(midasResult.absoluteVelocityAlert ? "SIM ⚡" : "não")}\n" +
                    $"  Texto ES   : \"{descricaoMidas}\""
                );

                TocarComandoDeVoz(comandoDecidido, passosObjeto, descricaoMidas);
                instrucaoAnterior = comandoDecidido;
            }
        }

        private void TocarComandoDeVoz(EstadoInstrucao comando, int passosObjeto = 0, string descricaoAmbiente = "")
        {
            float tempoDesteComando = tempoEsperaAcao;

            // Se o MiDaS já gerou uma descrição contextual do ambiente,
            // ela tem prioridade sobre a frase padrão do comando.
            bool temDescricaoMidas = !string.IsNullOrWhiteSpace(descricaoAmbiente);

            // ===============================================
            // LÓGICA 0: LLaMA LOCAL (Ollama no Notebook)
            // ===============================================
            if (usarLlamaText)
            {
                string textoPassosLlama = (passosObjeto == 1) ? "1 paso" : $"{passosObjeto} pasos";
                if (passosObjeto == 0) textoPassosLlama = "unos pasos";

                string fraseContexto = $"O usuário com deficiência visual está caminhando. A IA localizou um risco {textoPassosLlama} e decidiu: '{comando}'. Eres una inteligencia de voz infantil (niño). Escreva apenas 1 frase curtíssima em espanhol alertando ele ou guiando-o como o Pequeno Príncipe faria.";
                StartCoroutine(ProcessarEReproduzirLlama(fraseContexto, comando, tempoDesteComando));
                proximoTempoDeFala = Time.time + tempoDesteComando;
                return;
            }

            // ===============================================
            // LÓGICA 0.5: PIPER ONNX TTS ON-DEVICE (sem rede)
            // ===============================================
            if (piperOnnxTTS != null)
            {
                // Usa descrição contextual do MiDaS quando disponível
                string frase = temDescricaoMidas
                    ? descricaoAmbiente
                    : ObterFrasePadraoEspanholInfantil(comando, passosObjeto);

                // Timer pessimista até o clip estar pronto (evita race durante a síntese)
                proximoTempoDeFala = Time.time + tempoDesteComando;

                piperOnnxTTS.Sintetizar(frase, (AudioClip clip) =>
                {
                    if (clip != null && spatialAudio != null)
                    {
                        if (spatialAudio.ReproduziirClipPiper(clip, comando))
                        {
                            // FIX: Actualiza com a duração REAL do clip (já inclui pitchShiftFactor)
                            // + 0.4s de buffer para evitar corte no último fonema.
                            const float BUFFER_FINAL = 0.4f;
                            proximoTempoDeFala = Time.time + clip.length + BUFFER_FINAL;
                            Debug.Log($"[Guia] Timer actualizado: clip={clip.length:F2}s + buffer={BUFFER_FINAL}s " +
                                      $"→ próxima fala em {clip.length + BUFFER_FINAL:F2}s");
                        }
                    }
                    else
                    {
                        ExecutarAudioFallback(comando, tempoDesteComando);
                    }
                });
                return;
            }

            // ===============================================
            // LÓGICA 0.6: PIPER HTTP SERVER (notebook na rede)
            // ===============================================
            if (usarPiperLocal)
            {
                string textoBasePiper = ObterFrasePadraoEspanholInfantil(comando, passosObjeto);

                if (_coroutineTTSAtiva != null)
                {
                    StopCoroutine(_coroutineTTSAtiva);
                    _coroutineTTSAtiva = null;
                }
                _coroutineTTSAtiva = StartCoroutine(ConverterTextoParaPiperAudio(textoBasePiper, comando, tempoDesteComando));

                proximoTempoDeFala = Time.time + tempoDesteComando;
                return;
            }

            ExecutarAudioFallback(comando, tempoDesteComando);
        }

        private void ExecutarAudioFallback(EstadoInstrucao comando, float tempoDesteComando)
        {
            if (spatialAudio == null || spatialAudio.EstaReproduziindo) return;

            AudioClip clipParaTocar = null;

            switch (comando)
            {
                case EstadoInstrucao.Parar:               clipParaTocar = voiceStop;           tempoDesteComando = tempoEsperaParar;   break;
                case EstadoInstrucao.GirarDireita:        clipParaTocar = voiceTurnRight;                                              break;
                case EstadoInstrucao.GirarEsquerda:       clipParaTocar = voiceTurnLeft;                                               break;
                case EstadoInstrucao.DesviarDireita:      clipParaTocar = voiceMoveRight;                                              break;
                case EstadoInstrucao.DesviarEsquerda:     clipParaTocar = voiceMoveLeft;                                               break;
                case EstadoInstrucao.DesviarDuploDireita: clipParaTocar = voiceMoveDoubleRight;                                        break;
                case EstadoInstrucao.DesviarDuploEsquerda:clipParaTocar = voiceMoveDoubleLeft;                                         break;
                case EstadoInstrucao.Frente1:             clipParaTocar = voiceFrente1;        tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente2:             clipParaTocar = voiceFrente2;        tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente3:             clipParaTocar = voiceFrente3;        tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente4:             clipParaTocar = voiceFrente4;        tempoDesteComando = tempoEsperaContinuar; break;
            }

            if (clipParaTocar != null && spatialAudio.ReproduziirClipFallback(clipParaTocar, comando))
            {
                proximoTempoDeFala = Time.time + tempoDesteComando;
            }
        }

        private IEnumerator ProcessarEReproduzirLlama(string promptDeComando, EstadoInstrucao comando, float tempoDesteComando)
        {
            OllamaRequest reqData = new OllamaRequest
            {
                model = llamaModelName,
                prompt = promptDeComando,
                stream = false
            };

            string jsonData = JsonUtility.ToJson(reqData);

            using (UnityWebRequest webReq = new UnityWebRequest(llamaEndpointUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                webReq.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webReq.downloadHandler = new DownloadHandlerBuffer();
                webReq.SetRequestHeader("Content-Type", "application/json");

                // Timeout um pouco maior pois LLMs rodando no notebook podem demorar uns respiros a mais que a nuvem
                webReq.timeout = 6;

                yield return webReq.SendWebRequest();

                if (webReq.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[LLaMA Docker] Falha na rede do Hotspot: {webReq.error}");
                    // Fallback imediato se o notebook travar ou o Docker não estiver escutando
                    usarLlamaText = false;
                    ExecutarAudioFallback(comando, tempoDesteComando);
                }
                else
                {
                    try
                    {
                        OllamaResponse resData = JsonUtility.FromJson<OllamaResponse>(webReq.downloadHandler.text);
                        Debug.Log($"[LLaMA 3 | Notebook]: '{resData.response}'");
                        
                        // NOTA: Se o Piper TTS estiver ativado, processa o texto recém gerado em voz infantil pelo ONNX!
                        if (usarPiperLocal) 
                        {
                            StartCoroutine(ConverterTextoParaPiperAudio(resData.response, comando, tempoDesteComando));
                        }
                        else
                        {
                            ExecutarAudioFallback(comando, tempoDesteComando);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError("[LLaMA Docker] Erro ao deserializar o JSON do Ollama: " + ex.Message);
                        ExecutarAudioFallback(comando, tempoDesteComando);
                    }
                }
            }
        }

        private IEnumerator ConverterTextoParaPiperAudio(string texto, EstadoInstrucao comando, float tempoDesteComando)
        {
            string endpointStr = $"{piperEndpointUrl.TrimEnd('/')}/?text={UnityWebRequest.EscapeURL(texto)}";
            Debug.Log($"[Piper TTS] Iniciando síntese → '{texto}'");

            UnityWebRequest audioReq = null;
            bool sucesso = false;

            try
            {
                audioReq = UnityWebRequestMultimedia.GetAudioClip(endpointStr, AudioType.UNKNOWN);
                audioReq.timeout = 8;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Piper TTS] Erro ao criar request: {ex.Message}");
                ExecutarAudioFallback(comando, tempoDesteComando);
                _coroutineTTSAtiva = null;
                yield break;
            }

            using (audioReq)
            {
                yield return audioReq.SendWebRequest();

                // ============================================================
                // DIAGNÓSTICO: verifica se o servidor Piper gerou áudio real
                // ============================================================
                var    rawData     = audioReq.downloadHandler?.data;
                int    rawBytes    = rawData != null ? rawData.Length : 0;
                string contentType = audioReq.GetResponseHeader("Content-Type") ?? "(sem Content-Type)";
                bool   httpOk      = audioReq.result == UnityWebRequest.Result.Success && rawBytes > 44;

                if (httpOk)
                {
                    Debug.Log(
                        $"[Piper ✅ AUDIO GERADO]\n" +
                        $"  Texto       : '{texto}'\n" +
                        $"  HTTP Status : {audioReq.responseCode}\n" +
                        $"  Content-Type: {contentType}\n" +
                        $"  Tamanho WAV : {rawBytes:N0} bytes ({rawBytes / 1024f:F1} KB)"
                    );
                }
                else
                {
                    Debug.LogError(
                        $"[Piper ❌ AUDIO NÃO GERADO]\n" +
                        $"  Texto       : '{texto}'\n" +
                        $"  HTTP Status : {audioReq.responseCode} | Resultado: {audioReq.result}\n" +
                        $"  Erro        : {audioReq.error ?? "(nenhum)"}\n" +
                        $"  Content-Type: {contentType}\n" +
                        $"  Bytes       : {rawBytes} (esperado > 44 para WAV válido)"
                    );
                    ExecutarAudioFallback(comando, tempoDesteComando);
                    _coroutineTTSAtiva = null;
                    yield break;
                }
                // ============================================================

                AudioClip downloadedClip = null;
                try
                {
                    downloadedClip = DownloadHandlerAudioClip.GetContent(audioReq);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Piper TTS] Falha ao decodificar WAV: {ex.Message}");
                }

                if (downloadedClip == null)
                {
                    Debug.LogError("[Piper TTS] AudioClip retornado como null após GetContent.");
                }
                else
                {
                    Debug.Log($"[Piper TTS] Clip | LoadState: {downloadedClip.loadState} | Length: {downloadedClip.length:F2}s | Freq: {downloadedClip.frequency}Hz");

                    // Aguarda o clip terminar de carregar (evita Play() silencioso)
                    float espMax = 2.0f, espAc = 0f;
                    while (downloadedClip.loadState == AudioDataLoadState.Loading && espAc < espMax)
                    {
                        yield return null;
                        espAc += Time.deltaTime;
                    }

                    if (downloadedClip.loadState != AudioDataLoadState.Loaded)
                    {
                        Debug.LogError($"[Piper TTS] Clip não carregou em {espMax}s (estado: {downloadedClip.loadState}).");
                        Destroy(downloadedClip);
                    }
                    else if (downloadedClip.length <= 0f)
                    {
                        Debug.LogError("[Piper TTS] Clip com duração zero.");
                        Destroy(downloadedClip);
                    }
                    else if (spatialAudio != null && spatialAudio.ReproduziirClipPiper(downloadedClip, comando))
                    {
                        sucesso = true;
                    }
                    else
                    {
                        Debug.LogError("[Piper TTS] SpatialAudioGuide recusou o clip (AudioSource null?).");
                        Destroy(downloadedClip);
                    }
                }
            }

            if (!sucesso)
                ExecutarAudioFallback(comando, tempoDesteComando);

            _coroutineTTSAtiva = null;
        }

        private string ObterFrasePadraoEspanholInfantil(EstadoInstrucao comando, int passos)
        {
            // Sufixo de distância — usando diminutivos latinos (pasito, pasitos)
            string sufixo = passos == 1 ? " en un pasito."
                          : passos  > 1 ? $" en {passos} pasitos."
                          : ".";

            switch (comando)
            {
                case EstadoInstrucao.Parar:
                    return "¡Espera, espera! Hay algo justo enfrente. Mejor nos detenemos aquíto.";

                case EstadoInstrucao.GirarDireita:
                    return "¡Oye! Tenemos que girar hacia la derecha. ¡Así que vuelta, vuelta!";

                case EstadoInstrucao.GirarEsquerda:
                    return "¡Vamos! Hay que doblar por la izquierda. ¡Tú puedes, adelante!";

                case EstadoInstrucao.DesviarDireita:
                    return "¡Psst! Mueve un poquito hacia la derecha, ándale.";

                case EstadoInstrucao.DesviarEsquerda:
                    return "¡Psst! Deslizámonos un poquito a la izquierda, con calma.";

                case EstadoInstrucao.DesviarDuploDireita:
                    return "¡Uy, uy! Mueve bastante hacia la derecha" + sufixo;

                case EstadoInstrucao.DesviarDuploEsquerda:
                    return "¡Uy, uy! Hay que moverse harto hacia la izquierda" + sufixo;

                case EstadoInstrucao.Frente1:
                    return "¡Perfecto! El camino está libre. Sigue adelantito, con cuidadito.";

                case EstadoInstrucao.Frente2:
                    return "¡Qué bien! Puedes caminar tranquilito hacia adelante.";

                case EstadoInstrucao.Frente3:
                    return "¡Mira qué lindo camino tan libre! Sigamos adelante juntos.";

                case EstadoInstrucao.Frente4:
                    return "¡Super! Todo está despejadito. ¡Aventura hacia adelante!";

                default:
                    return "Caminando pasito a pasito, todo va bien.";
            }
        }

        // FIX ERRO 3: Ciclo de vida correto — para todas as corrotinas TTS ativas
        // e destrói o AudioClip dinâmico para liberar os native handles de memória
        // antes que o domain reload invalide os GC handles pendentes.
        private void OnDisable()
        {
            if (_coroutineTTSAtiva != null)
            {
                StopCoroutine(_coroutineTTSAtiva);
                _coroutineTTSAtiva = null;
            }
        }

        private void OnDestroy()
        {
            OnDisable();
            // _clipDinamicoAtual agora é gerenciado pelo SpatialAudioGuide.OnDestroy()
        }
    }
}