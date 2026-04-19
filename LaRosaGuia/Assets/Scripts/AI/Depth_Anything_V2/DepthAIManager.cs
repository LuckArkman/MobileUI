using UnityEngine;
using Unity.InferenceEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;

namespace LuckArkman.XR.AI
{
    // =========================================================================
    // JOB 1: Calcula o valor máximo do depth map em paralelo (Burst / SIMD).
    // Reutiliza o mesmo padrão do MiDaS para garantir consistência estrutural.
    // =========================================================================
    [BurstCompile]
    struct DAV2MaxDepthJob : IJob
    {
        [ReadOnly]  public NativeArray<float> depthArray;
        [WriteOnly] public NativeArray<float> maxDepthOut; // índice 0 = resultado

        public void Execute()
        {
            float max = 0.0001f;
            for (int i = 0; i < depthArray.Length; i++)
                if (depthArray[i] > max) max = depthArray[i];
            maxDepthOut[0] = max;
        }
    }

    // =========================================================================
    // JOB 2: Divide o depth map em zonas Esquerda / Centro / Direita.
    // 
    // DIFERENÇA CRÍTICA em relação ao MiDaS:
    //   MiDaS         → profundidade INVERSA: alto = LONGE = seguro → perigo = 1 - norm
    //   Depth Anything → profundidade DIRETA : alto = PERTO = perigo → perigo = norm
    //
    // A zona frontal usa apenas a metade INFERIOR da imagem (ignora teto e céu).
    // =========================================================================
    [BurstCompile]
    struct DAV2ZoneJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<float> depthArray;
        [ReadOnly]  public NativeArray<float> maxDepth;
        public int  tensorSize;
        public int  inverterEixoX; // 0=normal, 1=espelho horizontal

        [WriteOnly] public NativeArray<float> linhaEsquerda;
        [WriteOnly] public NativeArray<float> linhaDireita;
        [WriteOnly] public NativeArray<float> linhaCentral;
        [WriteOnly] public NativeArray<int>   countEsq;
        [WriteOnly] public NativeArray<int>   countDir;
        [WriteOnly] public NativeArray<int>   countCen;

        public void Execute(int y)
        {
            float somaE = 0f, somaD = 0f, somaC = 0f;
            int   cE    = 0,  cD    = 0,  cC    = 0;

            float maxD = maxDepth[0];
            if (maxD < 0.0001f) maxD = 0.0001f;

            int  terco       = tensorSize / 3;
            bool linhaFrontal = y > tensorSize / 2; // só metade inferior para centro

            for (int x = 0; x < tensorSize; x++)
            {
                float normalizado = depthArray[y * tensorSize + x] / maxD;
                if (normalizado > 1f) normalizado = 1f;

                // Depth Anything V2: profundidade DIRETA → alto = perto = PERIGO
                // Não é necessário inverter: perigo = normalizado
                float perigo = normalizado;

                int xReal = (inverterEixoX == 1) ? (tensorSize - 1 - x) : x;

                if (xReal < terco)
                {
                    somaE += perigo; cE++;
                }
                else if (xReal > terco * 2)
                {
                    somaD += perigo; cD++;
                }

                if (xReal >= terco && xReal <= terco * 2 && linhaFrontal)
                {
                    somaC += perigo; cC++;
                }
            }

            linhaEsquerda[y] = somaE;
            linhaDireita[y]  = somaD;
            linhaCentral[y]  = somaC;
            countEsq[y]      = cE;
            countDir[y]      = cD;
            countCen[y]      = cC;
        }
    }

    // =========================================================================
    // DepthAIManager — Motor de Profundidade com Depth Anything V2
    //
    // Mantém o mesmo contrato público que MidasInferenceManager:
    //   ExecuteInference(Texture2D) → MidasResult
    //
    // Isso permite ao MainSystemOrchestrator trocar de motor com um único flag
    // sem alterar nenhuma outra parte do pipeline (Decision, RaycastScanner, etc).
    //
    // Diferenças técnicas do modelo:
    //   Input : 1 × 3 × 518 × 518  (NCHW, float32, normalizado ImageNet)
    //   Output: 1 × 1 × 518 × 518  (profundidade relativa direta, 0–1 aprox.)
    //   Semântica: valor ALTO = objeto PERTO = PERIGO (oposto ao MiDaS original)
    // =========================================================================
    public class DepthAIManager : MonoBehaviour
    {
        // =====================================================================
        // SEÇÃO 1 — CONFIGURAÇÃO DO MODELO
        // =====================================================================

        [Header("Modelo Depth Anything V2")]
        [Tooltip(
            "Arraste o arquivo 'model_fp16.onnx' da pasta AI/Depth_Anything_V2.\n" +
            "O modelo aceita entrada 518×518 e produz um mapa de profundidade relativa direta."
        )]
        public ModelAsset modelAsset;

        [Header("Calibração de Câmara")]
        [Tooltip(
            "Inverte as zonas Esquerda/Direita do mapa de profundidade.\n" +
            "Ative se os comandos de evasão estiverem com lados trocados."
        )]
        public bool inverterEixoX = false;

        [Header("Detecção de Velocidade")]
        [Tooltip(
            "Delta de dangerScore por segundo necessário para acionar absoluteVelocityAlert.\n" +
            "Mesmos valores que o MiDaS — compatível com RaycastScanner e Decision."
        )]
        public float velocityThreshold = 2.5f;

        // =====================================================================
        // SEÇÃO 2 — ESTADO INTERNO
        // =====================================================================

        // Depth Anything V2 usa resolução 518×518 (múltiplo de 14, ViT patch size)
        private const int TENSOR_SIZE = 518;

        private Model  runtimeModel;
        private Worker engineWorker;

        private float previousDangerScore = 0f;
        private float lastProcessTime     = 0f;

        // =====================================================================
        // SEÇÃO 3 — CICLO DE VIDA UNITY
        // =====================================================================

        private void Start()
        {
            if (modelAsset == null)
            {
                Debug.LogError("[DepthAIManager] ❌ ModelAsset não atribuído no Inspector. " +
                               "Arraste o arquivo model_fp16.onnx da pasta AI/Depth_Anything_V2.");
                return;
            }

            runtimeModel = ModelLoader.Load(modelAsset);
            engineWorker = new Worker(runtimeModel, BackendType.GPUCompute);

            Debug.Log($"[DepthAIManager] ✅ Depth Anything V2 carregado. " +
                      $"Resolução de inferência: {TENSOR_SIZE}×{TENSOR_SIZE}px.");
        }

        private void OnDestroy()
        {
            engineWorker?.Dispose();
        }

        // =====================================================================
        // SEÇÃO 4 — INFERÊNCIA PRINCIPAL
        //
        // Mesmo contrato que MidasInferenceManager.ExecuteInference():
        //   Entrada : Texture2D da câmara ESP32
        //   Saída   : MidasResult com dangerScore / leftZoneDanger / rightZoneDanger
        //             e absoluteVelocityAlert baseado na variação temporal do score.
        // =====================================================================

        /// <summary>
        /// Executa a inferência do Depth Anything V2 e retorna um MidasResult.
        /// Compatível com todo o pipeline downstream (RaycastScanner, Decision, RouteProgressManager).
        /// </summary>
        public MidasResult ExecuteInference(Texture2D cameraImage)
        {
            MidasResult result = new MidasResult();
            if (engineWorker == null || cameraImage == null) return result;

            // ── 1. Tensor de Entrada ─────────────────────────────────────────
            // Depth Anything V2: entrada NCHW 1×3×518×518
            // TextureConverter escala automaticamente a textura para o tamanho do tensor
            TensorShape shape       = new TensorShape(1, 3, TENSOR_SIZE, TENSOR_SIZE);
            Tensor<float> inputTensor = new Tensor<float>(shape);
            TextureConverter.ToTensor(cameraImage, inputTensor, new TextureTransform());

            // ── 2. Inferência na GPU ─────────────────────────────────────────
            engineWorker.Schedule(inputTensor);
            Tensor<float> outputTensor = engineWorker.PeekOutput() as Tensor<float>;
            float[] depthArray = outputTensor.DownloadToArray();
            inputTensor.Dispose();

            // ── 3. Análise de Zonas (Burst / multi-thread) ───────────────────
            ProcessarZonasDeRiscoJob(depthArray, ref result);

            // ── 4. Detecção de Velocidade (Main Thread — custo mínimo) ───────
            float deltaTime = Time.time - lastProcessTime;
            if (deltaTime > 0f)
            {
                float depthDelta = (result.dangerScore - previousDangerScore) / deltaTime;
                result.absoluteVelocityAlert = depthDelta > velocityThreshold;
            }

            previousDangerScore = result.dangerScore;
            lastProcessTime     = Time.time;

            if (result.dangerScore >= 4.0f || result.absoluteVelocityAlert)
            {
                Debug.Log($"[DAV2 Radar] Frente: {result.dangerScore:F1}/10 | " +
                          $"Esq: {result.leftZoneDanger:F1}/10 | Dir: {result.rightZoneDanger:F1}/10 | " +
                          $"Velocidade: {(result.absoluteVelocityAlert ? "SIM ⚡" : "não")}");
            }

            return result;
        }

        // =====================================================================
        // SEÇÃO 5 — PROCESSAMENTO DE ZONAS VIA BURST JOBS
        // =====================================================================

        private void ProcessarZonasDeRiscoJob(float[] depthArrayManaged, ref MidasResult result)
        {
            var depthNative  = new NativeArray<float>(depthArrayManaged, Allocator.TempJob);
            var maxDepthOut  = new NativeArray<float>(1,           Allocator.TempJob);
            var linhaEsq     = new NativeArray<float>(TENSOR_SIZE, Allocator.TempJob);
            var linhaDir     = new NativeArray<float>(TENSOR_SIZE, Allocator.TempJob);
            var linhaCen     = new NativeArray<float>(TENSOR_SIZE, Allocator.TempJob);
            var countEsqArr  = new NativeArray<int>(TENSOR_SIZE,   Allocator.TempJob);
            var countDirArr  = new NativeArray<int>(TENSOR_SIZE,   Allocator.TempJob);
            var countCenArr  = new NativeArray<int>(TENSOR_SIZE,   Allocator.TempJob);

            // JOB 1: Máximo global (single-threaded, precisa concluir antes do JOB 2)
            var maxJob = new DAV2MaxDepthJob
            {
                depthArray  = depthNative,
                maxDepthOut = maxDepthOut
            };
            JobHandle maxHandle = maxJob.Schedule();

            // JOB 2: Zonas em paralelo por linha (depende do JOB 1 via chain)
            var zoneJob = new DAV2ZoneJob
            {
                depthArray    = depthNative,
                maxDepth      = maxDepthOut,
                tensorSize    = TENSOR_SIZE,
                inverterEixoX = inverterEixoX ? 1 : 0,
                linhaEsquerda = linhaEsq,
                linhaDireita  = linhaDir,
                linhaCentral  = linhaCen,
                countEsq      = countEsqArr,
                countDir      = countDirArr,
                countCen      = countCenArr,
            };
            // batchCount=8: 518 linhas / 8 = ~65 fatias por worker thread
            JobHandle zoneHandle = zoneJob.Schedule(TENSOR_SIZE, 8, maxHandle);
            zoneHandle.Complete();

            // Redução final: acumula somas das 518 linhas → 3 médias
            float somaE = 0f, somaD = 0f, somaC = 0f;
            int   cE    = 0,  cD    = 0,  cC    = 0;

            for (int y = 0; y < TENSOR_SIZE; y++)
            {
                somaE += linhaEsq[y]; cE += countEsqArr[y];
                somaD += linhaDir[y]; cD += countDirArr[y];
                somaC += linhaCen[y]; cC += countCenArr[y];
            }

            // Multiplica por 10 para manter escala 0–10 idêntica ao MiDaS
            result.leftZoneDanger  = cE > 0 ? (somaE / cE) * 10f : 0f;
            result.rightZoneDanger = cD > 0 ? (somaD / cD) * 10f : 0f;
            result.dangerScore     = cC > 0 ? (somaC / cC) * 10f : 0f;

            depthNative.Dispose();
            maxDepthOut.Dispose();
            linhaEsq.Dispose(); linhaDir.Dispose(); linhaCen.Dispose();
            countEsqArr.Dispose(); countDirArr.Dispose(); countCenArr.Dispose();
        }
    }
}
