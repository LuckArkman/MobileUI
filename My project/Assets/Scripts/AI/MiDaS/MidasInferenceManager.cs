using UnityEngine;
using Unity.InferenceEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using System;

namespace LuckArkman.XR.AI
{
    public struct MidasResult
    {
        public float dangerScore;           // 0–10: obstáculo ao centro-frente
        public bool  absoluteVelocityAlert; // objeto se aproximando rapidamente
        public float leftZoneDanger;        // 0–10: perigo zona esquerda
        public float rightZoneDanger;       // 0–10: perigo zona direita

        /// <summary>
        /// Gera uma descrição do ambiente em espanhol latino-americano infantil
        /// baseada nos scores de profundidade do MiDaS.
        /// Esta frase é enviada directamente ao Piper TTS para síntese de voz.
        /// </summary>
        public string GerarDescricaoEspanhol()
        {
            // Limiar de perigo: > 6.0 = obstáculo próximo, > 3.5 = atenção
            bool frentePerigoso  = dangerScore     > 6.0f;
            bool frenteAtencao   = dangerScore     > 3.5f;
            bool esqPerigoso     = leftZoneDanger  > 6.0f;
            bool dirPerigoso     = rightZoneDanger > 6.0f;
            bool esqAtencao      = leftZoneDanger  > 3.5f;
            bool dirAtencao      = rightZoneDanger > 3.5f;

            // ── Cenário 1: Algo muito perto não pode passar ─────────────────────
            if (frentePerigoso && esqPerigoso && dirPerigoso)
                return "¡Ay, ay, ay! Hay cosas por todos lados. Mejor nos detenemos un momentito.";

            if (frentePerigoso && absoluteVelocityAlert)
                return "¡Cuidado amigo! Algo se acerca rápido por el frente. ¡Para, para, para!";

            if (frentePerigoso && esqPerigoso)
                return "El camino del frente y de la izquierda está bloqueado. Giremos hacia la derecha.";

            if (frentePerigoso && dirPerigoso)
                return "Hay obstáculos al frente y a la derecha. Necesitamos ir hacia la izquierda.";

            if (frentePerigoso)
                return $"¡Uy! Hay algo muy cerca al frente, a unos {Mathf.RoundToInt(10f - dangerScore)} pasitos. Cuidadito.";

            // ── Cenário 2: Atenção moderada ────────────────────────────────────
            if (frenteAtencao && esqAtencao)
                return "Hay cosas cerca al frente y a la izquierda. Mejor nos movemos un poquito a la derecha.";

            if (frenteAtencao && dirAtencao)
                return "Algo está cerca al frente y a la derecha. Vamos ligerito hacia la izquierda.";

            if (frenteAtencao)
                return "Hay algo al frente, pero todavía podemos pasar con cuidadito.";

            // ── Cenário 3: Laterais com problema, centro livre ────────────────
            if (esqPerigoso && !dirAtencao)
                return "La izquierda está bloqueada, pero el camino a la derecha está libre. ¡Vamos!";

            if (dirPerigoso && !esqAtencao)
                return "La derecha está ocupada, pero por la izquierda está despejado. ¡Adelante!";

            if (esqAtencao && dirAtencao)
                return "Los costados tienen cosas cerca, pero el camino al frente está libre.";

            // ── Cenário 4: Caminho livre ─────────────────────────────────────
            if (absoluteVelocityAlert)
                return "El camino parece libre, pero algo se está moviendo cerca. Sigamos con calma.";

            return "El camino está despejado. Podemos caminar tranquilos hacia adelante.";
        }
    }

    // ============================================================
    // JOB 1: Encontra o valor máximo do depth array em paralelo.
    // [BurstCompile] compila para instrução nativa SIMD (ARM/x86),
    // eliminando o overhead do mono runtime C# no loop de 65K items.
    // ============================================================
    [BurstCompile]
    struct MidasMaxDepthJob : IJob
    {
        [ReadOnly]  public NativeArray<float> depthArray;
        [WriteOnly] public NativeArray<float> maxDepthOut; // índice 0 = resultado

        public void Execute()
        {
            float max = 0.0001f;
            for (int i = 0; i < depthArray.Length; i++)
            {
                if (depthArray[i] > max) max = depthArray[i];
            }
            maxDepthOut[0] = max;
        }
    }

    // ============================================================
    // JOB 2: Processa cada linha da imagem 256x256 em paralelo.
    // IJobParallelFor divide automaticamente as 256 linhas entre
    // os Worker Threads do Unity (normalmente 4–8 em mobile mid-end).
    // ============================================================
    [BurstCompile]
    struct MidasZoneJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<float> depthArray;
        [ReadOnly]  public NativeArray<float> maxDepth;  // lê maxDepthOut[0]
        public int tensorSize;

        // Acumuladores por linha (índice = linha Y)
        [WriteOnly] public NativeArray<float> linhaEsquerda;
        [WriteOnly] public NativeArray<float> linhaDireita;
        [WriteOnly] public NativeArray<float> linhaCentral;

        // Contadores por linha
        [WriteOnly] public NativeArray<int> countEsq;
        [WriteOnly] public NativeArray<int> countDir;
        [WriteOnly] public NativeArray<int> countCen;

        public void Execute(int y)
        {
            float somaE = 0f, somaD = 0f, somaC = 0f;
            int cE = 0, cD = 0, cC = 0;

            float maxD = maxDepth[0];
            int terco = tensorSize / 3;

            for (int x = 0; x < tensorSize; x++)
            {
                float pixelDepth = depthArray[y * tensorSize + x] / maxD;
                if (pixelDepth > 1f) pixelDepth = 1f;

                if (x < terco)
                {
                    somaE += pixelDepth; cE++;
                }
                else if (x > terco * 2)
                {
                    somaD += pixelDepth; cD++;
                }

                if (x >= terco && x <= terco * 2 && y > tensorSize / 2)
                {
                    somaC += pixelDepth; cC++;
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

    public class MidasInferenceManager : MonoBehaviour
    {
        [Header("Rede Neural MiDaS")]
        public ModelAsset modelAsset;

        [Header("Configurações Físicas")]
        public float velocityThreshold = 2.5f;

        private Model runtimeModel;
        private Worker engineWorker;
        private const int TENSOR_SIZE = 256;

        private float previousGlobalDepth = 0f;
        private float lastProcessTime = 0f;

        private void Start()
        {
            if (modelAsset != null)
            {
                runtimeModel = ModelLoader.Load(modelAsset);
                engineWorker = new Worker(runtimeModel, BackendType.GPUCompute);
            }
        }

        public MidasResult ExecuteInference(Texture2D cameraImage)
        {
            MidasResult result = new MidasResult();
            if (engineWorker == null || cameraImage == null) return result;

            // 1. Tensor de entrada
            TensorShape shape = new TensorShape(1, 3, TENSOR_SIZE, TENSOR_SIZE);
            Tensor<float> inputTensor = new Tensor<float>(shape);
            TextureConverter.ToTensor(cameraImage, inputTensor, new TextureTransform());

            // 2. Inferência na GPU (InferenceEngine - já é assíncrono na GPU)
            engineWorker.Schedule(inputTensor);
            Tensor<float> outputTensor = engineWorker.PeekOutput() as Tensor<float>;
            float[] depthArray = outputTensor.DownloadToArray();
            inputTensor.Dispose();

            // 3. Processa zonas com Jobs Burst (multithreaded na CPU)
            ProcessarZonasDeRiscoJob(depthArray, ref result);

            // 4. Cálculo de velocidade (leve, permanece na main thread)
            float deltaTime = Time.time - lastProcessTime;
            if (deltaTime > 0)
            {
                float depthDelta = (result.dangerScore - previousGlobalDepth) / deltaTime;
                result.absoluteVelocityAlert = depthDelta > velocityThreshold;
            }

            previousGlobalDepth = result.dangerScore;
            lastProcessTime = Time.time;

            if (result.dangerScore >= 4.0f || result.absoluteVelocityAlert)
            {
                Debug.Log($"[MiDaS Radar] Frente: {result.dangerScore:F1}/10 | Esq: {result.leftZoneDanger:F1}/10 | Dir: {result.rightZoneDanger:F1}/10 | Alerta Movimento: {(result.absoluteVelocityAlert ? "SIM" : "NÃO")}");
            }

            return result;
        }

        private void ProcessarZonasDeRiscoJob(float[] depthArrayManaged, ref MidasResult result)
        {
            int total = depthArrayManaged.Length;

            // Aloca NativeArrays (sem GC, Burst-compatible)
            var depthNative   = new NativeArray<float>(depthArrayManaged, Allocator.TempJob);
            var maxDepthOut   = new NativeArray<float>(1,          Allocator.TempJob);
            var linhaEsq      = new NativeArray<float>(TENSOR_SIZE, Allocator.TempJob);
            var linhaDir      = new NativeArray<float>(TENSOR_SIZE, Allocator.TempJob);
            var linhaCen      = new NativeArray<float>(TENSOR_SIZE, Allocator.TempJob);
            var countEsqArr   = new NativeArray<int>(TENSOR_SIZE,   Allocator.TempJob);
            var countDirArr   = new NativeArray<int>(TENSOR_SIZE,   Allocator.TempJob);
            var countCenArr   = new NativeArray<int>(TENSOR_SIZE,   Allocator.TempJob);

            // JOB 1: Calcula máximo (depende de nada — roda imediatamente)
            var maxJob = new MidasMaxDepthJob
            {
                depthArray = depthNative,
                maxDepthOut = maxDepthOut
            };
            JobHandle maxHandle = maxJob.Schedule();

            // JOB 2: Processa zonas em paralelo por linha (depende do JOB 1)
            var zoneJob = new MidasZoneJob
            {
                depthArray    = depthNative,
                maxDepth      = maxDepthOut,
                tensorSize    = TENSOR_SIZE,
                linhaEsquerda = linhaEsq,
                linhaDireita  = linhaDir,
                linhaCentral  = linhaCen,
                countEsq      = countEsqArr,
                countDir      = countDirArr,
                countCen      = countCenArr,
            };
            // innerloopBatchCount=4: cada worker thread processa 4 linhas por fatia
            JobHandle zoneHandle = zoneJob.Schedule(TENSOR_SIZE, 4, maxHandle);

            // Sincroniza — bloqueia a main thread apenas aqui (resultado pronto)
            zoneHandle.Complete();

            // Redução final (256 somas → 3 médias, custo desprezível)
            float somaE = 0f, somaD = 0f, somaC = 0f;
            int cE = 0, cD = 0, cC = 0;
            for (int y = 0; y < TENSOR_SIZE; y++)
            {
                somaE += linhaEsq[y]; cE += countEsqArr[y];
                somaD += linhaDir[y]; cD += countDirArr[y];
                somaC += linhaCen[y]; cC += countCenArr[y];
            }

            result.leftZoneDanger  = cE > 0 ? (somaE / cE) * 10f : 0f;
            result.rightZoneDanger = cD > 0 ? (somaD / cD) * 10f : 0f;
            result.dangerScore     = cC > 0 ? (somaC / cC) * 10f : 0f;

            // Libera NativeArrays — sem GC spike
            depthNative.Dispose();
            maxDepthOut.Dispose();
            linhaEsq.Dispose(); linhaDir.Dispose(); linhaCen.Dispose();
            countEsqArr.Dispose(); countDirArr.Dispose(); countCenArr.Dispose();
        }

        private void OnDestroy()
        {
            engineWorker?.Dispose();
        }
    }
}