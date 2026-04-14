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
        }

        public void ExecutarComando(EstadoInstrucao comandoDecidido, int passosObjeto = 0, string descricaoAmbiente = "")
        {
            if (Time.time >= proximoTempoDeFala)
            {
                if (comandoDecidido != instrucaoAnterior)
                {
                    TocarComandoDeVoz(comandoDecidido, passosObjeto, descricaoAmbiente);
                    instrucaoAnterior = comandoDecidido;
                }
            }
        }

        private void TocarComandoDeVoz(EstadoInstrucao comando, int passosObjeto = 0, string descricaoAmbiente = "")
        {
            float tempoDesteComando = tempoEsperaAcao;
            // A direção do som é aplicada dentro de cada método da SpatialAudioGuide

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
            // LÓGICA 0.5: PIPER ONNX TTS ISOLADO (Sem Gen-Text, Apenas Voz)
            // ===============================================
            if (usarPiperLocal)
            {
                string textoBasePiper = ObterFrasePadraoEspanholInfantil(comando, passosObjeto);

                // FIX ERRO 2: Cancela a corrotina anterior antes de iniciar uma nova.
                // Impede que duas falas se sobreponham ou congestionem o servidor Piper.
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
            Debug.Log($"[Piper TTS] Enviando request â€º '{endpointStr}'");

            UnityWebRequest audioReq = null;
            bool sucesso = false;

            try
            {
                // FIX: AudioType.UNKNOWN instrui o Unity a detectar o formato
                // automaticamente pelo Content-Type do servidor, sem rejeitar
                // WAVs cujos cabeçalhos não correspondam exatamente ao padrão.
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
                // DIAGNÓSTICO PIPER: verifica se o servidor gerou áudio real
                // ============================================================
                var rawData  = audioReq.downloadHandler?.data;
                int rawBytes = rawData != null ? rawData.Length : 0;
                string contentType = audioReq.GetResponseHeader("Content-Type") ?? "(sem Content-Type)";

                if (audioReq.result == UnityWebRequest.Result.Success && rawBytes > 44) // WAV header = 44 bytes
                {
                    Debug.Log(
                        $"[Piper ✅ AUDIO GERADO]\n" +
                        $"  Texto sintetizado : '{texto}'\n" +
                        $"  Servidor          : {piperEndpointUrl}\n" +
                        $"  HTTP Status        : {audioReq.responseCode}\n" +
                        $"  Content-Type       : {contentType}\n" +
                        $"  Tamanho WAV        : {rawBytes:N0} bytes ({rawBytes / 1024f:F1} KB)"
                    );
                }
                else
                {
                    Debug.LogError(
                        $"[Piper ❌ AUDIO NÃO GERADO]\n" +
                        $"  Texto enviado      : '{texto}'\n" +
                        $"  Servidor           : {piperEndpointUrl}\n" +
                        $"  HTTP Status        : {audioReq.responseCode} | Resultado: {audioReq.result}\n" +
                        $"  Erro               : {audioReq.error ?? "(nenhum)"}\n" +
                        $"  Content-Type       : {contentType}\n" +
                        $"  Bytes recebidos    : {rawBytes} (esperado > 44 para WAV válido)"
                    );
                }
                // ============================================================

                if (audioReq.result != UnityWebRequest.Result.Success || rawBytes <= 44)
                {
                    ExecutarAudioFallback(comando, tempoDesteComando);
                    _coroutineTTSAtiva = null;
                    yield break;
                }

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
                        Debug.Log($"[Piper TTS] Clip criado | LoadState: {downloadedClip.loadState} | Length: {downloadedClip.length:F2}s | Freq: {downloadedClip.frequency}Hz | Ch: {downloadedClip.channels}");

                        // FIX CRÍTICO: Aguarda o AudioClip terminar de carregar.
                        // GetContent() é assíncrono internamente no Unity — tentar Play()
                        // com loadState != Loaded resulta em silêncio total sem erro.
                        float esperaMaxima = 2.0f;
                        float esperaAcumulada = 0f;
                        while (downloadedClip.loadState == AudioDataLoadState.Loading && esperaAcumulada < esperaMaxima)
                        {
                            yield return null; // espera um frame
                            esperaAcumulada += Time.deltaTime;
                        }

                        Debug.Log($"[Piper TTS] LoadState após espera: {downloadedClip.loadState} ({esperaAcumulada * 1000:F0}ms)");

                        if (downloadedClip.loadState != AudioDataLoadState.Loaded)
                        {
                            Debug.LogError($"[Piper TTS] Clip não carregou em {esperaMaxima}s (estado: {downloadedClip.loadState}). Usando fallback.");
                            Destroy(downloadedClip);
                        }
                        else if (downloadedClip.length <= 0f)
                        {
                            Debug.LogError("[Piper TTS] Clip carregado mas com duração zero. O servidor pode ter enviado WAV vazio.");
                            Destroy(downloadedClip);
                        }
                        else
                        {
                            if (spatialAudio != null && spatialAudio.ReproduziirClipPiper(downloadedClip, comando))
                            {
                                sucesso = true;
                            }
                            else
                            {
                                Debug.LogError("[Piper TTS] SpatialAudioGuide recusou o clip (AudioSource null?). Veja o Inspector.");
                                Destroy(downloadedClip);
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[Piper TTS] Falha HTTP {audioReq.responseCode}: {audioReq.error}");
                }
            }

            if (!sucesso)
            {
                ExecutarAudioFallback(comando, tempoDesteComando);
            }

            _coroutineTTSAtiva = null;
        }

        private string ObterFrasePadraoEspanholInfantil(EstadoInstrucao comando, int passos)
        {
            string fimPassos = passos > 0 ? (passos == 1 ? " en un pasito." : $" en {passos} pasos.") : ".";

            switch (comando)
            {
                case EstadoInstrucao.Parar:               return "¡Oh! Detente ahí, tenemos algo adelante.";
                case EstadoInstrucao.GirarDireita:        return "Giremos hacia la derecha por favor.";
                case EstadoInstrucao.GirarEsquerda:       return "Vamos hacia tu lado izquierdo, amigo mío.";
                case EstadoInstrucao.DesviarDireita:      return "Un pequeño desvío hacia la derecha ahora.";
                case EstadoInstrucao.DesviarEsquerda:     return "A tu izquierda, un ligero desvío.";
                case EstadoInstrucao.DesviarDuploDireita: return "Movámonos hacia la derecha por precaución" + fimPassos;
                case EstadoInstrucao.DesviarDuploEsquerda: return "Avancemos a la izquierda para cuidarte" + fimPassos;
                case EstadoInstrucao.Frente1:             return "Todo despejado, sigue adelante con cuidado.";
                case EstadoInstrucao.Frente2:             return "Podemos caminar tranquilos hacia adelante.";
                case EstadoInstrucao.Frente3:             return "El camino al frente es hermoso y libre.";
                case EstadoInstrucao.Frente4:             return "Aventura adelante, ¡está totalmente libre!";
                default:                                  return "Caminando, paso a pasito.";
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