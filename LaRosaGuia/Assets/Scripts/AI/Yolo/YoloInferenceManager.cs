using UnityEngine;
using Unity.InferenceEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using System.Collections.Generic;

namespace LuckArkman.XR.AI
{
    [global::System.Serializable]
    public struct DetectionResult
    {
        public Rect  box;
        public float confidence;
        public int   classId;
        public FixedString64Bytes label; // Burst-compatible: sem managed string
    }

    // ============================================================
    // JOB: Parser YOLOv8 paralelizado por caixa (IJobParallelFor).
    // Cada índice i = 1 das 8400 caixas candidatas.
    // Os 8400 trabalhos são distribuídos entre todos os Worker Threads
    // disponíveis pelo Unity Job Scheduler (ARM big.LITTLE-aware).
    // ============================================================
    [BurstCompile]
    struct YoloParseJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<float>  output;
        [ReadOnly]  public float               confidenceThreshold;
        public int numBoxes;   // 8400
        public int numClasses; // 80

        // Saída: confidence e classId por slot (slot inativo = confidence < 0)
        [WriteOnly] public NativeArray<float> outConfidence;
        [WriteOnly] public NativeArray<int>   outClassId;
        [WriteOnly] public NativeArray<float> outXcNorm;
        [WriteOnly] public NativeArray<float> outYcNorm;
        [WriteOnly] public NativeArray<float> outWNorm;
        [WriteOnly] public NativeArray<float> outHNorm;

        public void Execute(int i)
        {
            float maxClassProb = 0f;
            int   maxClassId   = -1;

            for (int c = 0; c < numClasses; c++)
            {
                float prob = output[(4 + c) * numBoxes + i];
                if (prob > maxClassProb) { maxClassProb = prob; maxClassId = c; }
            }

            if (maxClassProb >= confidenceThreshold)
            {
                outConfidence[i] = maxClassProb;
                outClassId[i]    = maxClassId;
                outXcNorm[i]     = output[0 * numBoxes + i] / 640f;
                outYcNorm[i]     = output[1 * numBoxes + i] / 640f;
                outWNorm[i]      = output[2 * numBoxes + i] / 640f;
                outHNorm[i]      = output[3 * numBoxes + i] / 640f;
            }
            else
            {
                // Marca slot como inativo
                outConfidence[i] = -1f;
                outClassId[i]    = -1;
                outXcNorm[i]     = 0f;
                outYcNorm[i]     = 0f;
                outWNorm[i]      = 0f;
                outHNorm[i]      = 0f;
            }
        }
    }

    // ============================================================
    // JOB: Calcula IoU entre a caixa "current" e todas as demais.
    // Usado dentro do NMS para eliminar sobreposições em paralelo.
    // ============================================================
    [BurstCompile]
    struct IoUMarkJob : IJobParallelFor
    {
        public float currentXMin, currentYMin, currentXMax, currentYMax;
        public float iouThreshold;

        [ReadOnly]  public NativeArray<float> xMin;
        [ReadOnly]  public NativeArray<float> yMin;
        [ReadOnly]  public NativeArray<float> xMax;
        [ReadOnly]  public NativeArray<float> yMax;
        [ReadOnly]  public NativeArray<byte>  active;   // 1 = ativo, 0 = removido

        [WriteOnly] public NativeArray<byte>  markRemove; // 1 = remover

        public void Execute(int i)
        {
            if (active[i] == 0) { markRemove[i] = 0; return; }

            float interX1 = currentXMin > xMin[i] ? currentXMin : xMin[i];
            float interY1 = currentYMin > yMin[i] ? currentYMin : yMin[i];
            float interX2 = currentXMax < xMax[i] ? currentXMax : xMax[i];
            float interY2 = currentYMax < yMax[i] ? currentYMax : yMax[i];

            float interW = interX2 - interX1;
            float interH = interY2 - interY1;

            if (interW <= 0f || interH <= 0f) { markRemove[i] = 0; return; }

            float intersection = interW * interH;
            float areaA = (currentXMax - currentXMin) * (currentYMax - currentYMin);
            float areaB = (xMax[i] - xMin[i]) * (yMax[i] - yMin[i]);
            float iou   = intersection / (areaA + areaB - intersection);

            markRemove[i] = iou >= iouThreshold ? (byte)1 : (byte)0;
        }
    }

    public class YoloInferenceManager : MonoBehaviour
    {
        [Header("Cérebro da IA")]
        public ModelAsset modelAsset;
        private Worker worker;
        private Model  model;

        [Header("Filtros de Visão")]
        [Range(0f, 1f)] public float confidenceThreshold = 0.25f;
        public float iouThreshold = 0.45f;

        private readonly string[] CocoClasses = new string[]
        {
            "Pessoa", "Bicicleta", "Carro", "Moto", "Aviao", "Onibus", "Trem", "Caminhao", "Barco", "Semaforo",
            "Hidrante", "Placa Pare", "Parquimetro", "Banco", "Passaro", "Gato", "Cachorro", "Cavalo", "Ovelha", "Vaca",
            "Elefante", "Urso", "Zebra", "Girafa", "Mochila", "Guarda-chuva", "Bolsa", "Gravata", "Mala", "Frisbee",
            "Esquis", "Prancha Snowboard", "Bola Esportes", "Pipa", "Taco Beisebol", "Luva Beisebol", "Skate", "Prancha Surf", "Raquete Tenis", "Garrafa",
            "Copo", "Xicara", "Garfo", "Faca", "Colher", "Tigela", "Banana", "Maca", "Sanduiche", "Laranja",
            "Brocolis", "Cenoura", "Cachorro Quente", "Pizza", "Donut", "Bolo", "Cadeira", "Sofa", "Planta", "Cama",
            "Mesa", "Privada", "Monitor", "Laptop", "Mouse", "Controle Remoto", "Teclado", "Celular", "Microondas", "Forno",
            "Torradeira", "Pia", "Geladeira", "Livro", "Relogio", "Vaso", "Tesoura", "Ursinho Pelucia", "Secador", "Escova de Dentes"
        };

        private const int NUM_BOXES   = 8400;
        private const int NUM_CLASSES = 80;

        private void Start()
        {
            if (modelAsset != null)
            {
                model  = ModelLoader.Load(modelAsset);
                worker = new Worker(model, BackendType.GPUCompute);
            }
        }

        public List<DetectionResult> ExecuteInference(Texture2D sourceTexture)
        {
            var results = new List<DetectionResult>();
            if (worker == null || sourceTexture == null) return results;

            // GPU Inference via InferenceEngine
            TensorShape shape = new TensorShape(1, 3, 640, 640);
            Tensor<float> inputTensor = new Tensor<float>(shape);
            TextureConverter.ToTensor(sourceTexture, inputTensor, new TextureTransform());
            worker.Schedule(inputTensor);

            Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
            float[] outputData = outputTensor.DownloadToArray();
            inputTensor.Dispose();

            // Parse + NMS com Jobs Burst (multithreaded na CPU)
            results = ParseYoloV8Burst(outputData, sourceTexture.width, sourceTexture.height);
            return results;
        }

        private List<DetectionResult> ParseYoloV8Burst(float[] outputManaged, int imageWidth, int imageHeight)
        {
            // --- Aloca NativeArrays para o Job de Parse ---
            var outputNative  = new NativeArray<float>(outputManaged, Allocator.TempJob);
            var outConf       = new NativeArray<float>(NUM_BOXES, Allocator.TempJob);
            var outClass      = new NativeArray<int>  (NUM_BOXES, Allocator.TempJob);
            var outXc         = new NativeArray<float>(NUM_BOXES, Allocator.TempJob);
            var outYc         = new NativeArray<float>(NUM_BOXES, Allocator.TempJob);
            var outW          = new NativeArray<float>(NUM_BOXES, Allocator.TempJob);
            var outH          = new NativeArray<float>(NUM_BOXES, Allocator.TempJob);

            // JOB: Parse de 8400 caixas × 80 classes em paralelo
            var parseJob = new YoloParseJob
            {
                output              = outputNative,
                confidenceThreshold = confidenceThreshold,
                numBoxes            = NUM_BOXES,
                numClasses          = NUM_CLASSES,
                outConfidence       = outConf,
                outClassId          = outClass,
                outXcNorm           = outXc,
                outYcNorm           = outYc,
                outWNorm            = outW,
                outHNorm            = outH,
            };
            // batchCount=32: 8400 caixas ÷ 32 = ~262 grupos de trabalho paralelo
            JobHandle parseHandle = parseJob.Schedule(NUM_BOXES, 32);
            parseHandle.Complete();

            outputNative.Dispose();

            // --- Coleta candidatos válidos (apenas os activos) ---
            // Convertemos para listas gerenciadas para o NMS (que precisa de sort)
            var candidates = new List<DetectionResult>(64);
            for (int i = 0; i < NUM_BOXES; i++)
            {
                if (outConf[i] < 0f) continue;

                float wPx = outW[i] * imageWidth;
                float hPx = outH[i] * imageHeight;
                float xPx = outXc[i] * imageWidth  - wPx * 0.5f;
                float yPx = outYc[i] * imageHeight - hPx * 0.5f;

                int classId = outClass[i];
                candidates.Add(new DetectionResult
                {
                    box        = new Rect(xPx, yPx, wPx, hPx),
                    confidence = outConf[i],
                    classId    = classId,
                    label      = classId >= 0 && classId < CocoClasses.Length
                                     ? new FixedString64Bytes(CocoClasses[classId])
                                     : new FixedString64Bytes("Desconhecido")
                });
            }

            outConf.Dispose(); outClass.Dispose();
            outXc.Dispose(); outYc.Dispose(); outW.Dispose(); outH.Dispose();

            // --- NMS com IoUMarkJob paralelo ---
            List<DetectionResult> finalResults = ApplyNMSBurst(candidates);

            // Log de resultado (apenas detecções confiantes acima de 50%)
            foreach (var r in finalResults)
            {
                if (r.confidence > 0.50f)
                {
                    float perigo = GetPerigo(r.label.ToString());
                    Debug.Log($"[YOLO] Identificado: {r.label} | Certeza: {r.confidence * 100:F1}% | Periculosidade: {perigo}/5");
                }
            }

            return finalResults;
        }

        private List<DetectionResult> ApplyNMSBurst(List<DetectionResult> boxes)
        {
            int n = boxes.Count;
            if (n == 0) return boxes;

            // Ordena por confidence (decrescente) na main thread — lista pequena
            boxes.Sort((a, b) => b.confidence.CompareTo(a.confidence));

            // NativeArrays com bounding boxes para o IoUMarkJob
            var xMinArr = new NativeArray<float>(n, Allocator.TempJob);
            var yMinArr = new NativeArray<float>(n, Allocator.TempJob);
            var xMaxArr = new NativeArray<float>(n, Allocator.TempJob);
            var yMaxArr = new NativeArray<float>(n, Allocator.TempJob);
            var active  = new NativeArray<byte> (n, Allocator.TempJob);
            var markRem = new NativeArray<byte> (n, Allocator.TempJob);

            for (int i = 0; i < n; i++)
            {
                xMinArr[i] = boxes[i].box.xMin;
                yMinArr[i] = boxes[i].box.yMin;
                xMaxArr[i] = boxes[i].box.xMax;
                yMaxArr[i] = boxes[i].box.yMax;
                active[i]  = 1;
            }

            var result = new List<DetectionResult>(n);

            for (int cur = 0; cur < n; cur++)
            {
                if (active[cur] == 0) continue;

                result.Add(boxes[cur]);

                // Marca sobreposições com IoU >= threshold em paralelo
                var iouJob = new IoUMarkJob
                {
                    currentXMin = boxes[cur].box.xMin,
                    currentYMin = boxes[cur].box.yMin,
                    currentXMax = boxes[cur].box.xMax,
                    currentYMax = boxes[cur].box.yMax,
                    iouThreshold = iouThreshold,
                    xMin        = xMinArr,
                    yMin        = yMinArr,
                    xMax        = xMaxArr,
                    yMax        = yMaxArr,
                    active      = active,
                    markRemove  = markRem
                };
                JobHandle iouHandle = iouJob.Schedule(n, 16);
                iouHandle.Complete();

                // Aplica marcações (desativa slots com IoU excessivo)
                for (int k = cur + 1; k < n; k++)
                {
                    if (markRem[k] == 1) active[k] = 0;
                }
            }

            xMinArr.Dispose(); yMinArr.Dispose();
            xMaxArr.Dispose(); yMaxArr.Dispose();
            active.Dispose();  markRem.Dispose();

            return result;
        }

        private float GetPerigo(string label)
        {
            switch (label.ToLower())
            {
                case "carro": case "caminhao": case "onibus": case "moto": case "aviao": return 5.0f;
                case "bicicleta": case "trem":                                            return 4.0f;
                case "pessoa": case "semaforo": case "hidrante": case "placa pare":       return 3.0f;
                case "cadeira": case "cachorro": case "gato": case "mochila":             return 1.0f;
                default:                                                                  return 2.5f;
            }
        }

        private void OnDestroy()
        {
            worker?.Dispose();
        }
    }
}