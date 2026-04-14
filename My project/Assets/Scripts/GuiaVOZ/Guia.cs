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

        [Header("Generative AI: Gemini 2.5 Flash Audio")]
        [Tooltip("Usa a nova API Native Audio Dialog do Gemini para compor falas doces de menino dinamicamente.")]
        public bool usarGeminiAudio = true;
        public string apiKey = "AIzaSyBwrf3zdf6Kwm10SDTPUdQxx8Tvl0J3rTY";

        [Header("Generative AI: LLaMA (Ollama Docker Notebook)")]
        [Tooltip("Ative para usar o modelo LLaMA local no seu notebook conectado no roteador do celular.")]
        public bool usarLlamaText = false;
        public string llamaEndpointUrl = "http://192.168.43.10:11434/api/generate";
        public string llamaModelName = "llama3";

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

        // Estrutura do Novo Gemini 2.5 Native Audio
        [System.Serializable] private class GeminiRequest { public GeminiContent[] contents; public GeminiGenConfig generationConfig; }
        [System.Serializable] private class GeminiContent { public string role; public GeminiPart[] parts; }
        [System.Serializable] private class GeminiPart { public string text; }
        [System.Serializable] private class GeminiGenConfig { public string[] responseModalities; public GeminiSpeechConfig speechConfig; }
        [System.Serializable] private class GeminiSpeechConfig { public GeminiVoiceConfig voiceConfig; }
        [System.Serializable] private class GeminiVoiceConfig { public GeminiPrebuiltConfig prebuiltVoiceConfig; }
        [System.Serializable] private class GeminiPrebuiltConfig { public string voiceName; }

        [System.Serializable] private class GeminiResponse { public GeminiCandidate[] candidates; }
        [System.Serializable] private class GeminiCandidate { public GeminiResponseContent content; }
        [System.Serializable] private class GeminiResponseContent { public GeminiResponsePart[] parts; }
        [System.Serializable] private class GeminiResponsePart { public GeminiInlineData inlineData; }
        [System.Serializable] private class GeminiInlineData { public string mimeType; public string data; }

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
            if (spatialAudio != null) spatialAudio.AjustarDirecaoDoSom(comando);

            // ===============================================
            // LÓGICA 0: LLaMA LOCAL (Ollama no Notebook)
            // ===============================================
            if (usarLlamaText)
            {
                string textoPassosLlama = (passosObjeto == 1) ? "1 paso" : $"{passosObjeto} pasos";
                if (passosObjeto == 0) textoPassosLlama = "unos pasos";

                string fraseContexto = $"O usuário com deficiência visual está caminhando. A IA localizou um risco {textoPassosLlama} e decidiu: '{comando}'. Aja como uma inteligência guia. Escreva apenas 1 frase curtíssima em espanhol alertando ele ou guiando-o.";
                StartCoroutine(ProcessarEReproduzirLlama(fraseContexto, comando, tempoDesteComando));
                proximoTempoDeFala = Time.time + tempoDesteComando;
                return;
            }

            // ===============================================
            // LÓGICA 1: GEMINI 2.5 FLASH NATIVE AUDIO DIALOG
            // ===============================================
            if (usarGeminiAudio)
            {
                if (!string.IsNullOrEmpty(descricaoAmbiente))
                {
                    StartCoroutine(ProcessarEReproduzirGeminiTTS(descricaoAmbiente, comando, tempoDesteComando));
                    proximoTempoDeFala = Time.time + tempoDesteComando;
                }
                return; 
            }

            ExecutarAudioFallback(comando, tempoDesteComando);
        }

        private void ExecutarAudioFallback(EstadoInstrucao comando, float tempoDesteComando)
        {
            if (spatialAudio == null || spatialAudio.voiceAudioSource == null || spatialAudio.voiceAudioSource.isPlaying) return;

            AudioClip clipParaTocar = null;

            switch (comando)
            {
                case EstadoInstrucao.Parar: clipParaTocar = voiceStop; tempoDesteComando = tempoEsperaParar; break;
                case EstadoInstrucao.GirarDireita: clipParaTocar = voiceTurnRight; break;
                case EstadoInstrucao.GirarEsquerda: clipParaTocar = voiceTurnLeft; break;
                case EstadoInstrucao.DesviarDireita: clipParaTocar = voiceMoveRight; break;
                case EstadoInstrucao.DesviarEsquerda: clipParaTocar = voiceMoveLeft; break;
                case EstadoInstrucao.DesviarDuploDireita: clipParaTocar = voiceMoveDoubleRight; break;
                case EstadoInstrucao.DesviarDuploEsquerda: clipParaTocar = voiceMoveDoubleLeft; break;
                case EstadoInstrucao.Frente1: clipParaTocar = voiceFrente1; tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente2: clipParaTocar = voiceFrente2; tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente3: clipParaTocar = voiceFrente3; tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente4: clipParaTocar = voiceFrente4; tempoDesteComando = tempoEsperaContinuar; break;
            }

            if (clipParaTocar != null)
            {
                spatialAudio.voiceAudioSource.clip = clipParaTocar;
                spatialAudio.voiceAudioSource.Play();
                proximoTempoDeFala = Time.time + tempoDesteComando;
            }
        }

        private IEnumerator ProcessarEReproduzirGeminiTTS(string descricaoDaCenaRadar, EstadoInstrucao comando, float tempoDesteComando)
        {
            // O modelo Gemini Flash Live é forçado especificamente quando há falha do standard no Audio Dialog
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            // O Contexto modificado força a IA a olhar para a cena descrita e formular a voz baseada no ambiente
            string promptPersona = $"Genera obligatoriamente tu respuesta como una pista de audio (Native Audio) descriptiva. Eres un perro guía y debes narrar este escenario al ciego basándote EN LOS DATOS DEL RADAR a continuación y la acción de proteger '{comando}'. Datos del Radar actual: '{descricaoDaCenaRadar}'. NO retornes texto, usa la modalidad de audio. Sé muy conciso y directo en una sola frase.";

            GeminiRequest reqData = new GeminiRequest
            {
                contents = new GeminiContent[] {
                    new GeminiContent {
                        role = "user",
                        parts = new GeminiPart[] { new GeminiPart { text = promptPersona } }
                    }
                },
                generationConfig = new GeminiGenConfig {
                    responseModalities = new string[] { "AUDIO" },
                    speechConfig = new GeminiSpeechConfig {
                        voiceConfig = new GeminiVoiceConfig {
                            // "Puck" é descrito oficialmente pela Google como neutro/androgino e jovem
                            prebuiltVoiceConfig = new GeminiPrebuiltConfig { voiceName = "Puck" }
                        }
                    }
                }
            };

            string jsonData = JsonUtility.ToJson(reqData);

            using (UnityWebRequest webReq = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                webReq.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webReq.downloadHandler = new DownloadHandlerBuffer();
                webReq.SetRequestHeader("Content-Type", "application/json");

                yield return webReq.SendWebRequest();

                if (webReq.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Gemini 2.5 Audio] Limite da API ou falha de rede: {webReq.error}");
                    
                    if (webReq.responseCode == 429)
                    {
                        usarGeminiAudio = false;
                        Debug.LogWarning("[Gemini 2.5 Audio] Quota de requisições gratuitas excedida (429). A IA generativa ficará adormecida nesta sessão, recorrendo aos clipes físicos locais.");
                    }
                    
                    ExecutarAudioFallback(comando, tempoDesteComando);
                }
                else
                {
                    string audioBase64 = null;
                    try
                    {
                        GeminiResponse resData = JsonUtility.FromJson<GeminiResponse>(webReq.downloadHandler.text);
                        if (resData.candidates != null && resData.candidates.Length > 0 && resData.candidates[0].content.parts.Length > 0)
                        {
                            var inlineData = resData.candidates[0].content.parts[0].inlineData;
                            if (inlineData != null && !string.IsNullOrEmpty(inlineData.data))
                            {
                                audioBase64 = inlineData.data;
                            }
                            else
                            {
                                Debug.LogWarning("[Gemini 2.5 Audio] A Resposta do Gemini não retornou fragmento de áudio inline/Wav. Verifique sua permissão Modalidade.");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError("[Gemini 2.5 Audio] Erro crítico ao decodificar a estrutura Cloud Json: " + ex.Message);
                    }

                    if (!string.IsNullOrEmpty(audioBase64))
                    {
                        string tempPath = Path.Combine(Application.temporaryCachePath, "gemini_voice.wav");
                        bool gravado = false;
                        
                        try
                        {
                            byte[] audioWavBytes = System.Convert.FromBase64String(audioBase64);
                            File.WriteAllBytes(tempPath, audioWavBytes);
                            gravado = true;
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError("[Gemini 2.5 Audio] Erro crítico ao decodificar e salvar a Base64: " + ex.Message);
                        }

                        if (gravado)
                        {
                            string fileUri = "file://" + tempPath;
                            using (UnityWebRequest audioReq = UnityWebRequestMultimedia.GetAudioClip(fileUri, AudioType.WAV))
                            {
                                yield return audioReq.SendWebRequest();
                                
                                if (audioReq.result == UnityWebRequest.Result.Success)
                                {
                                    AudioClip downloadedClip = DownloadHandlerAudioClip.GetContent(audioReq);
                                    if (downloadedClip != null)
                                    {
                                        if (spatialAudio != null && spatialAudio.voiceAudioSource != null)
                                        {
                                            spatialAudio.voiceAudioSource.clip = downloadedClip;
                                            spatialAudio.voiceAudioSource.Play();
                                        }
                                        Debug.Log($"[Gemini 2.5 Native Audio Dialog]: Som reproduzido descrevendo a cena no áudio 3D.");
                                    }
                                }
                                else
                                {
                                    Debug.LogError($"[Cloud TTS] Erro ao instanciar buffer gerado: {audioReq.error}");
                                    ExecutarAudioFallback(comando, tempoDesteComando);
                                }
                            }
                        }
                        else
                        {
                            ExecutarAudioFallback(comando, tempoDesteComando);
                        }
                    }
                    else
                    {
                        ExecutarAudioFallback(comando, tempoDesteComando);
                    }
                }
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
                        
                        // NOTA: Como o LLaMA é puro texto, usamos o sistema local físico de voz
                        // Simultaneamente para não travar o usuário enquanto a IA não possui um módulo TTS.
                        ExecutarAudioFallback(comando, tempoDesteComando);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError("[LLaMA Docker] Erro ao deserializar o JSON do Ollama: " + ex.Message);
                        ExecutarAudioFallback(comando, tempoDesteComando);
                    }
                }
            }
        }
    }
}