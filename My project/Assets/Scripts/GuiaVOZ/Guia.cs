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

        private void Start()
        {
            if (voiceAudioSource == null)
            {
                voiceAudioSource = gameObject.AddComponent<AudioSource>();
                voiceAudioSource.playOnAwake = false;
            }
        }

        public void ExecutarComando(EstadoInstrucao comandoDecidido, int passosObjeto = 0)
        {
            if (Time.time >= proximoTempoDeFala)
            {
                if (comandoDecidido != instrucaoAnterior)
                {
                    TocarComandoDeVoz(comandoDecidido, passosObjeto);
                    instrucaoAnterior = comandoDecidido;
                }
            }
        }

        private void TocarComandoDeVoz(EstadoInstrucao comando, int passosObjeto = 0)
        {
            float tempoDesteComando = tempoEsperaAcao;
            if (spatialAudio != null) spatialAudio.AjustarDirecaoDoSom(comando);

            // ===============================================
            // LÓGICA 1: GEMINI 2.5 FLASH NATIVE AUDIO
            // ===============================================
            if (usarGeminiAudio)
            {
                string fraseEspanhol = "";
                string textoPassos = (passosObjeto == 1) ? "1 paso" : $"{passosObjeto} pasos";
                if (passosObjeto == 0) textoPassos = "unos pasos";

                switch (comando)
                {
                    case EstadoInstrucao.Parar: fraseEspanhol = "¡Cuidado! Detente, por favor."; tempoDesteComando = tempoEsperaParar; break;
                    case EstadoInstrucao.GirarDireita: fraseEspanhol = "Gira a la derecha."; break;
                    case EstadoInstrucao.GirarEsquerda: fraseEspanhol = "Gira a la izquierda."; break;
                    case EstadoInstrucao.DesviarDireita: fraseEspanhol = $"Obstáculo a {textoPassos}, vamos a la derecha."; break;
                    case EstadoInstrucao.DesviarEsquerda: fraseEspanhol = $"Obstáculo a {textoPassos}, vamos a la izquierda."; break;
                    case EstadoInstrucao.DesviarDuploDireita: fraseEspanhol = $"Atención a {textoPassos}, doble paso a la derecha."; break;
                    case EstadoInstrucao.DesviarDuploEsquerda: fraseEspanhol = $"Atención a {textoPassos}, doble paso a la izquierda."; break;
                    case EstadoInstrucao.Frente1: fraseEspanhol = "El camino está libre. Sigamos explorando."; tempoDesteComando = tempoEsperaContinuar; break;
                    case EstadoInstrucao.Frente2: 
                    case EstadoInstrucao.Frente3: 
                    case EstadoInstrucao.Frente4: 
                        fraseEspanhol = "Podemos seguir adelante."; tempoDesteComando = tempoEsperaContinuar; break;
                }

                if (!string.IsNullOrEmpty(fraseEspanhol))
                {
                    StartCoroutine(ProcessarEReproduzirGeminiTTS(fraseEspanhol));
                    proximoTempoDeFala = Time.time + tempoDesteComando;
                }
                return; 
            }

            // ===============================================
            // LÓGICA 2: ARQUIVOS DE ÁUDIO LEGADOS (FALLBACK)
            // ===============================================
            if (voiceAudioSource == null || voiceAudioSource.isPlaying) return;

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
                voiceAudioSource.clip = clipParaTocar;
                voiceAudioSource.Play();
                proximoTempoDeFala = Time.time + tempoDesteComando;
            }
        }

        private IEnumerator ProcessarEReproduzirGeminiTTS(string fraseAviso)
        {
            // Endpoint Oficial para Gemini 2.5 Flash Native Audio Dialog 
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            // O Contexto do Pequeno Príncipe forçando a IA a atuar e cuspir apenas áudio sem explicações de chat
            string promptPersona = $"Lee la siguiente instrucción del sistema en voz alta. Utiliza un tono infantil, dulce y tranquilo. Comportate como el personaje de un niño guiando a alguien. No añadas introducciones ni conversaciones extra, solo lee estrictamente la instrucción: \"{fraseAviso}\"";

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
                    Debug.LogError($"[Gemini 2.5 Audio] Falha na síntese cognitiva: {webReq.error} - {webReq.downloadHandler.text}");
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
                                        voiceAudioSource.clip = downloadedClip;
                                        voiceAudioSource.Play();
                                        Debug.Log($"[Gemini 2.5 Native Audio | Puck Voice]: '{fraseAviso}'");
                                    }
                                }
                                else
                                {
                                    Debug.LogError($"[Cloud TTS] Erro ao instanciar buffer gerado: {audioReq.error}");
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}