using UnityEngine;
using Unity.InferenceEngine; 
using System.Collections.Generic;
using System.Linq;

namespace LuckArkman.XR.AI
{
    [System.Serializable]
    public struct DetectionResult
    {
        public Rect box;
        public float confidence;
        public int classId;
        public string label;
    }

    public class YoloInferenceManager : MonoBehaviour
    {
        [Header("Cérebro da IA")]
        public ModelAsset modelAsset;
        private Worker worker;
        private Model model;

        [Header("Filtros de Visão")]
        [Range(0f, 1f)] 
        public float confidenceThreshold = 0.25f; 
        public float iouThreshold = 0.45f; 

        private readonly string[] CocoClasses = new string[] {
            "Pessoa", "Bicicleta", "Carro", "Moto", "Aviao", "Onibus", "Trem", "Caminhao", "Barco", "Semaforo",
            "Hidrante", "Placa Pare", "Parquimetro", "Banco", "Passaro", "Gato", "Cachorro", "Cavalo", "Ovelha", "Vaca",
            "Elefante", "Urso", "Zebra", "Girafa", "Mochila", "Guarda-chuva", "Bolsa", "Gravata", "Mala", "Frisbee",
            "Esquis", "Prancha Snowboard", "Bola Esportes", "Pipa", "Taco Beisebol", "Luva Beisebol", "Skate", "Prancha Surf", "Raquete Tenis", "Garrafa",
            "Copo", "Xicara", "Garfo", "Faca", "Colher", "Tigela", "Banana", "Maca", "Sanduiche", "Laranja",
            "Brocolis", "Cenoura", "Cachorro Quente", "Pizza", "Donut", "Bolo", "Cadeira", "Sofa", "Planta", "Cama",
            "Mesa", "Privada", "Monitor", "Laptop", "Mouse", "Controle Remoto", "Teclado", "Celular", "Microondas", "Forno",
            "Torradeira", "Pia", "Geladeira", "Livro", "Relogio", "Vaso", "Tesoura", "Ursinho Pelucia", "Secador", "Escova de Dentes"
        };

        private void Start()
        {
            if (modelAsset == null)
            {
                Debug.LogError("[YoloAI] ERRO CRÍTICO: ModelAsset não está atribuído no Inspector! " +
                               "Arraste o arquivo .sentis para o campo 'Model Asset' do YoloInferenceManager.");
                return;
            }

            StartCoroutine(InitializeWorkerDelayed());
        }

        /// <summary>
        /// Inicializa o Worker de IA com um frame de atraso.
        /// Isso garante que o contexto gráfico do Android (OpenGLES/Vulkan)
        /// já esteja 100% inicializado antes de tentarmos alocar memória na GPU.
        /// Sem este atraso, drivers Mali e Adreno podem causar crash silencioso.
        /// </summary>
        private System.Collections.IEnumerator InitializeWorkerDelayed()
        {
            // Aguarda 2 frames para garantir que o contexto gráfico está estável.
            yield return null;
            yield return null;

            try
            {
                model = ModelLoader.Load(modelAsset);

                // Tenta a GPU primeiro (melhor performance em mobile)
                try
                {
                    worker = new Worker(model, BackendType.GPUCompute);
                    Debug.Log("[YoloAI] Worker inicializado com sucesso: Backend GPU (Compute).");
                }
                catch (System.Exception gpuEx)
                {
                    // Fallback para CPU se o driver de GPU não suportar Compute Shaders
                    Debug.LogWarning($"[YoloAI] GPU Compute indisponível ({gpuEx.Message}). " +
                                     "Usando CPU como fallback. Performance reduzida.");
                    worker = new Worker(model, BackendType.CPU);
                    Debug.Log("[YoloAI] Worker inicializado com fallback: Backend CPU.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[YoloAI] Falha CRÍTICA ao carregar modelo: {e.Message}. " +
                               "Verifique se o arquivo .sentis é válido e compatível com esta versão do Inference Engine.");
            }
        }

        public List<DetectionResult> ExecuteInference(Texture2D sourceTexture)
        {
            List<DetectionResult> results = new List<DetectionResult>();

            // Guarda de segurança: worker pode ser null se a GPU ainda está inicializando
            // ou se o modelo não foi atribuído. Retorna lista vazia sem crash.
            if (worker == null || sourceTexture == null) return results;

            TensorShape shape = new TensorShape(1, 3, 640, 640);
            Tensor<float> inputTensor = new Tensor<float>(shape);
            
            TextureTransform transform = new TextureTransform();
            TextureConverter.ToTensor(sourceTexture, inputTensor, transform);

            worker.Schedule(inputTensor);

            Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
            
            // NOVO: Verificação de segurança (Erro de Cache/GPU)
            if (outputTensor == null) 
            {
                Debug.LogWarning("[YoloAI] Falha na recuperação do tensor da GPU. O cache pode estar instável.");
                inputTensor.Dispose();
                return results;
            }

            float[] outputData = outputTensor.DownloadToArray();

            results = ParseYoloV8(outputData, sourceTexture.width, sourceTexture.height);

            inputTensor.Dispose();

            return results;
        }

        private List<DetectionResult> ParseYoloV8(float[] output, int imageWidth, int imageHeight)
        {
            List<DetectionResult> candidates = new List<DetectionResult>();
            int numClasses = 80;
            int numBoxes = 8400;

            float absolutaMaiorCerteza = 0f; 

            for (int i = 0; i < numBoxes; i++)
            {
                float maxClassProb = 0f;
                int maxClassId = -1;

                for (int c = 0; c < numClasses; c++)
                {
                    float prob = output[(4 + c) * numBoxes + i];
                    if (prob > maxClassProb)
                    {
                        maxClassProb = prob;
                        maxClassId = c;
                    }
                }

                if (maxClassProb > absolutaMaiorCerteza)
                {
                    absolutaMaiorCerteza = maxClassProb;
                }

                if (maxClassProb >= confidenceThreshold)
                {
                    float xcNorm = output[0 * numBoxes + i] / 640f; 
                    float ycNorm = output[1 * numBoxes + i] / 640f;
                    float wNorm = output[2 * numBoxes + i] / 640f;
                    float hNorm = output[3 * numBoxes + i] / 640f;

                    float realWidth = wNorm * imageWidth;
                    float realHeight = hNorm * imageHeight;
                    float realX = (xcNorm * imageWidth) - (realWidth / 2f);
                    float realY = (ycNorm * imageHeight) - (realHeight / 2f);

                    candidates.Add(new DetectionResult
                    {
                        box = new Rect(realX, realY, realWidth, realHeight),
                        confidence = maxClassProb,
                        classId = maxClassId,
                        label = CocoClasses[maxClassId]
                    });

                    // Debug.Log($"[IA Direção] Achei {CocoClasses[maxClassId]}. Centro X real da foto: {xcNorm * imageWidth:F1}..."); // -> COMENTADO
                }
            }

            // Debug.Log($"[Raio-X da IA] A maior certeza neste frame foi: {absolutaMaiorCerteza * 100:F1}%"); // -> COMENTADO

            // Pega a lista limpa de sobreposições
            List<DetectionResult> finalResults = ApplyNMS(candidates);

            // =========================================================================================
            // NOVO LOG: Apenas avisa se a certeza for > 50%, já indicando o nível de perigo
            // =========================================================================================
            foreach (var result in finalResults)
            {
                if (result.confidence > 0.50f)
                {
                    float perigo = 2.5f; // Nota média padrão para objetos desconhecidos

                    // Espelha as notas de letalidade lá do Decision.cs
                    switch (result.label.ToLower())
                    {
                        case "carro": case "caminhao": case "onibus": case "moto": case "aviao":
                            perigo = 5.0f; break;
                        case "bicicleta": case "trem":
                            perigo = 4.0f; break;
                        case "pessoa": case "semaforo": case "hidrante": case "placa pare":
                            perigo = 3.0f; break;
                        case "cadeira": case "cachorro": case "gato": case "mochila":
                            perigo = 1.0f; break;
                    }

                    Debug.Log($"[YOLO] Identificado: {result.label} | Certeza: {result.confidence * 100:F1}% | Periculosidade: {perigo}/5");
                }
            }

            return finalResults;
        }

        private List<DetectionResult> ApplyNMS(List<DetectionResult> boxes)
        {
            var result = new List<DetectionResult>();
            var sortedBoxes = boxes.OrderByDescending(b => b.confidence).ToList();

            while (sortedBoxes.Count > 0)
            {
                var current = sortedBoxes[0];
                result.Add(current);
                sortedBoxes.RemoveAt(0);

                sortedBoxes.RemoveAll(b => CalculateIoU(current.box, b.box) >= iouThreshold);
            }
            return result;
        }

        private float CalculateIoU(Rect boxA, Rect boxB)
        {
            float xA = Mathf.Max(boxA.xMin, boxB.xMin);
            float yA = Mathf.Max(boxA.yMin, boxB.yMin);
            float xB = Mathf.Min(boxA.xMax, boxB.xMax);
            float yB = Mathf.Min(boxA.yMax, boxB.yMax);

            float intersectionArea = Mathf.Max(0, xB - xA) * Mathf.Max(0, yB - yA);
            return intersectionArea / ((boxA.width * boxA.height) + (boxB.width * boxB.height) - intersectionArea);
        }

        private void OnDestroy()
        {
            worker?.Dispose();
        }
    }
}