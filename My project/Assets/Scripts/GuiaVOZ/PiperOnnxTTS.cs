using UnityEngine;
using Unity.InferenceEngine;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;

namespace LuckArkman.XR.Main
{
    /// <summary>
    /// Motor TTS Piper ONNX 100% on-device.
    /// Converte texto em espanhol → IDs de fonemas → inferência ONNX → AudioClip.
    /// Não requer servidor Python nem conexão de rede.
    ///
    /// Parâmetros do modelo es_MX-ald-medium:
    ///   sample_rate : 22050 Hz
    ///   noise_scale : 0.667
    ///   length_scale: 1.0
    ///   noise_w     : 0.8
    /// </summary>
    public class PiperOnnxTTS : MonoBehaviour
    {
        [Header("Modelo Piper ONNX")]
        [Tooltip("Arraste o arquivo es_MX-ald-medium.onnx aqui.")]
        public ModelAsset piperModelAsset;

        [Header("Parâmetros de Síntese")]
        [Tooltip("Variação de entoação (0.1=monotono, 0.667=natural, 2.0=muito expressivo).")]
        [Range(0.1f, 2.0f)] public float noiseScale  = 0.667f;

        [Tooltip("Velocidade da fala gerada pelo modelo. >1 = mais lento. Use 1.25 com pitchShiftFactor=1.30.")]
        [Range(0.5f, 2.0f)] public float lengthScale = 1.25f;

        [Tooltip("Variação de duração das sílabas (0=robótico, 0.9=natural infantil).")]
        [Range(0.0f, 1.0f)] public float noiseW      = 0.9f;

        [Header("Tom Infantil (Pitch Shift por Resample)")]
        [Tooltip(
            "Fator de deslocamento de pitch.\n" +
            "1.00 = voz adulta original do modelo.\n" +
            "1.20 = voz jovem/adolescente.\n" +
            "1.30 = voz criança (recomendado).\n" +
            "1.50 = voz muito aguda.\n\n" +
            "Técnica: declara o AudioClip com sample rate menor (SAMPLE_RATE / factor),\n" +
            "o Unity reproduz mais rápido e mais agudo — sem DSP pesado."
        )]
        [Range(1.0f, 1.8f)] public float pitchShiftFactor = 1.30f;

        // Tokens especiais do protocolo Piper VITS
        private const long ID_PAD   = 0;  // _  padding
        private const long ID_BOS   = 1;  // ^  início de frase
        private const long ID_EOS   = 2;  // $  fim de frase
        private const long ID_SPACE = 3;  // espaço entre palavras

        private const int SAMPLE_RATE = 22050;

        private Model  _model;
        private Worker _worker;
        private bool   _pronto = false;

        // ── Mapa de fonema (IPA) → ID, extraído do es_MX-ald-medium.onnx.json ──────────
        // Letras latinas básicas (espeak as emite directamente para espanhol):
        private static readonly Dictionary<char, long> _phonemeIdMap = new Dictionary<char, long>
        {
            {'a', 14}, {'b', 15}, {'c', 16}, {'d', 17}, {'e', 18}, {'f', 19},
            {'h', 20}, {'i', 21}, {'j', 22}, {'k', 23}, {'l', 24}, {'m', 25},
            {'n', 26}, {'o', 27}, {'p', 28}, {'q', 29}, {'r', 30}, {'s', 31},
            {'t', 32}, {'u', 33}, {'v', 34}, {'w', 35}, {'x', 36}, {'y', 37},
            {'z', 38},
            // Pontuação
            {'!',  4}, {'\'', 5}, {'(', 6}, {')', 7}, {',', 8}, {'-', 9},
            {'.', 10}, {':', 11}, {';', 12}, {'?', 13},
            // IPA usados internamente pelo espeak para es-419:
            // ɲ (nh/ñ)=82, ɾ (r simples)=92, β (b/v intervocálico)=125,
            // ð (d intervocálico)=41, ɣ (g intervocálico)=68, ʎ (ll)=104
            // ʝ (y consonante Argentina/Mx)=115, ŋ (n antes de k/g)=44
            {'\u0272', 82},  // ɲ
            {'\u027E', 92},  // ɾ
            {'\u03B2', 125}, // β
            {'\u00F0', 41},  // ð
            {'\u0263', 68},  // ɣ
            {'\u028E', 104}, // ʎ
            {'\u029D', 115}, // ʝ
            {'\u014B', 44},  // ŋ
            {'\u02C8', 120}, // ˈ (acento primário)
            {'\u02CC', 121}, // ˌ (acento secundário)
        };

        // ── G2P: Grafema para Fonema — Espanhol Latino-Americano ────────────────────────
        // O espanhol é altamente fonético; as regras abaixo cobrem 99%+ dos casos
        // dos textos de navegação gerados por ObterFrasePadraoEspanholInfantil.
        private static string TextoParaFonemas(string texto)
        {
            texto = texto.ToLower().Trim();
            var fonemas = new System.Text.StringBuilder();

            for (int i = 0; i < texto.Length; i++)
            {
                char c  = texto[i];
                char cn = i + 1 < texto.Length ? texto[i + 1] : '\0'; // próximo char
                char cp = i > 0               ? texto[i - 1] : '\0'; // char anterior

                // Dígrafos e casos especiais ──────────────────────────────────────────
                if (c == 'c' && cn == 'h') { fonemas.Append("tʃ"); i++; continue; } // ch → tʃ
                if (c == 'l' && cn == 'l') { fonemas.Append('\u028E'); i++; continue; } // ll → ʎ (mx)
                if (c == 'r' && cn == 'r') { fonemas.Append('r'); i++; continue; }         // rr → r trill
                if (c == 'q' && cn == 'u') { i++; continue; }                              // qu → silent u (som k)
                if (c == 'g' && cn == 'u' && (i + 2 < texto.Length) &&
                    (texto[i + 2] == 'e' || texto[i + 2] == 'i')) { i++; continue; }       // gue/gui → g

                // Letras individuais ──────────────────────────────────────────────────
                switch (c)
                {
                    // Vogais com tilde (mesma vogal, só mudam acento na prosódia)
                    case 'á': fonemas.Append('\u02C8'); fonemas.Append('a'); break;
                    case 'é': fonemas.Append('\u02C8'); fonemas.Append('e'); break;
                    case 'í': fonemas.Append('\u02C8'); fonemas.Append('i'); break;
                    case 'ó': fonemas.Append('\u02C8'); fonemas.Append('o'); break;
                    case 'ú': fonemas.Append('\u02C8'); fonemas.Append('u'); break;
                    case 'ü': fonemas.Append('w');  break; // ü → w (güe)

                    // Consoantes especiais
                    case 'ñ': fonemas.Append('\u0272'); break;  // ñ → ɲ
                    case 'h': break;                             // h é mudo em espanhol

                    // c → s antes de e/i (Latin American: ceceo não existe)
                    case 'c':
                        fonemas.Append(cn == 'e' || cn == 'i' ? 's' : 'k');
                        break;

                    // g → x antes de e/i
                    case 'g':
                        fonemas.Append(cn == 'e' || cn == 'i' ? 'x' : 'g');
                        break;

                    // j → x (som como "jota")
                    case 'j': fonemas.Append('x'); break;

                    // r inicial de palavra ou após n/l/s → trill; senão tap ɾ
                    case 'r':
                        bool eInicial = (cp == '\0' || cp == ' ');
                        bool aposConsoante = (cp == 'n' || cp == 'l' || cp == 's');
                        fonemas.Append(eInicial || aposConsoante ? 'r' : '\u027E');
                        break;

                    // v → b em espanhol (não há distinção fonética)
                    case 'v': fonemas.Append('b'); break;

                    // z → s em espanhol latino-americano
                    case 'z': fonemas.Append('s'); break;

                    // x → ks em geral
                    case 'x': fonemas.Append('k'); fonemas.Append('s'); break;

                    // y → ʝ como consoante, → i como vogal sozinha
                    case 'y':
                        bool yVogal = (cn == '\0' || cn == ' ' || cn == '.' || cn == ',');
                        fonemas.Append(yVogal ? 'i' : '\u029D');
                        break;

                    // Espaço → separador de palavra
                    case ' ': fonemas.Append(' '); break;

                    // Pontuação → passa directo para o token map
                    case '.': case ',': case '!': case '?':
                    case ';': case ':': case '-': case '\'':
                        fonemas.Append(c);
                        break;

                    // Caracteres não mapeados (¡ ¿) → ignorar
                    case '¡': case '¿': break;

                    // Letras normais passam directamente (a,b,d,e,f,i,k,l,m,n,o,p,s,t,u,w)
                    default:
                        if (c >= 'a' && c <= 'z') fonemas.Append(c);
                        break;
                }
            }

            return fonemas.ToString();
        }

        // ── Converte string de fonemas IPA em array de IDs Piper ─────────────────────
        private static long[] FontemasParaIds(string fonemas)
        {
            var ids = new List<long>();
            ids.Add(ID_BOS); // ^ início

            string[] palavras = fonemas.Split(' ');
            for (int w = 0; w < palavras.Length; w++)
            {
                if (w > 0) ids.Add(ID_SPACE); // espaço entre palavras

                foreach (char phone in palavras[w])
                {
                    if (_phonemeIdMap.TryGetValue(phone, out long id))
                        ids.Add(id);
                    // Fonema não mapeado: ignora silenciosamente
                }
            }

            ids.Add(ID_EOS); // $ fim
            return ids.ToArray();
        }

        // ── Ciclo de vida Unity ──────────────────────────────────────────────────────
        private void Awake()
        {
            if (piperModelAsset == null)
            {
                Debug.LogError("[PiperOnnxTTS] ModelAsset não atribuído no Inspector!");
                return;
            }
            _model  = ModelLoader.Load(piperModelAsset);
            _worker = new Worker(_model, BackendType.GPUCompute);
            _pronto = true;
            Debug.Log("[PiperOnnxTTS ✅] Modelo ONNX carregado. TTS on-device pronto.");
        }

        private void OnDestroy()
        {
            _worker?.Dispose();
        }

        // ── API Pública ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Sintetiza o texto em espanhol e entrega o AudioClip ao callback.
        /// Toda a inferência é feita on-device, sem rede.
        /// </summary>
        public void Sintetizar(string texto, System.Action<AudioClip> onPronto)
        {
            if (!_pronto)
            {
                Debug.LogError("[PiperOnnxTTS] Motor não está pronto.");
                onPronto?.Invoke(null);
                return;
            }
            StartCoroutine(SintetizarCoroutine(texto, onPronto));
        }

        private IEnumerator SintetizarCoroutine(string texto, System.Action<AudioClip> onPronto)
        {
            Debug.Log($"[PiperOnnxTTS] Sintetizando: '{texto}'");

            // 1. Texto → Fonemas (G2P)
            string fonemas = TextoParaFonemas(texto);
            long[] ids     = FontemasParaIds(fonemas);
            Debug.Log($"[PiperOnnxTTS] Fonemas: [{string.Join(", ", ids)}] ({ids.Length} tokens)");

            if (ids.Length < 3) // apenas BOS+EOS = inválido
            {
                Debug.LogError("[PiperOnnxTTS] Sequência de fonemas vazia.");
                onPronto?.Invoke(null);
                yield break;
            }

            // 2. Cria tensores de entrada (Piper VITS ONNX interface)
            TensorShape inputShape  = new TensorShape(1, ids.Length);
            TensorShape lenShape    = new TensorShape(1);
            TensorShape scaleShape  = new TensorShape(3);

            Tensor<int> inputTensor = new Tensor<int>(inputShape);
            Tensor<int> lenTensor   = new Tensor<int>(lenShape);
            Tensor<float> scaleTensor = new Tensor<float>(scaleShape);

            for (int i = 0; i < ids.Length; i++)
                inputTensor[0, i] = (int)ids[i];

            lenTensor[0] = ids.Length;

            scaleTensor[0] = noiseScale;
            scaleTensor[1] = lengthScale;
            scaleTensor[2] = noiseW;

            // 3. Inferência na GPU
            _worker.SetInput("input",         inputTensor);
            _worker.SetInput("input_lengths",  lenTensor);
            _worker.SetInput("scales",         scaleTensor);
            _worker.Schedule();

            // Yield um frame para não bloquear a Main Thread durante a inferência
            yield return null;

            // 4. Obtém amostras de áudio (shape: [1, 1, T_audio])
            Tensor<float> outputTensor = _worker.PeekOutput() as Tensor<float>;
            if (outputTensor == null)
            {
                Debug.LogError("[PiperOnnxTTS] Output tensor é null. Verifique o modelo ONNX.");
                inputTensor.Dispose(); lenTensor.Dispose(); scaleTensor.Dispose();
                onPronto?.Invoke(null);
                yield break;
            }

            float[] amostras = outputTensor.DownloadToArray();
            inputTensor.Dispose();
            lenTensor.Dispose();
            scaleTensor.Dispose();

            if (amostras == null || amostras.Length == 0)
            {
                Debug.LogError("[PiperOnnxTTS] Amostras de áudio vazias.");
                onPronto?.Invoke(null);
                yield break;
            }

            // 5. Converte float[] → AudioClip Unity (PCM 32-bit, mono, 22050 Hz)
            float duracao = (float)amostras.Length / SAMPLE_RATE;

            // ── PITCH SHIFT POR RESAMPLE ────────────────────────────────────────────
            // Declara o AudioClip com sample rate menor que o real.
            // Unity interpreta: "este áudio foi gravado mais devagar" e reproduz
            // mais rápido e mais agudo — efeito de voz infantil sem DSP pesado.
            // Exemplo: SAMPLE_RATE=22050, factor=1.30 → declara 16961 Hz
            //          Unity toca 22050/16961 = 1.30x mais rápido e agudo.
            int playSampleRate = Mathf.Max(8000, Mathf.RoundToInt(SAMPLE_RATE / pitchShiftFactor));

            Debug.Log(
                $"[PiperOnnxTTS ✅ AUDIO GERADO]\n" +
                $"  Amostras     : {amostras.Length:N0}\n" +
                $"  Duração real  : {duracao:F2}s @ {SAMPLE_RATE}Hz\n" +
                $"  PitchFactor  : {pitchShiftFactor:F2}x (sample rate declarado: {playSampleRate}Hz)\n" +
                $"  Duração reproduzida: {(float)amostras.Length / playSampleRate:F2}s (mais curta = mais aguda)"
            );

            AudioClip clip = AudioClip.Create("PiperTTS", amostras.Length, 1, playSampleRate, false);
            clip.SetData(amostras, 0);

            onPronto?.Invoke(clip);
        }
    }
}
