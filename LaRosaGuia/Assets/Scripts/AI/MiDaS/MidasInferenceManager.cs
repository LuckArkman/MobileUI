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

        // Contador estático de rotação — garante variação mesmo com mesma condição
        private static int _rotacao;

        /// <summary>
        /// Gera uma descrição DINÂMICA do ambiente em espanhol latino-americano infantil.
        /// As frases são compostas usando os valores numéricos reais dos scores MiDaS,
        /// com rotação de variantes para evitar repetição em loop.
        /// </summary>
        public string GerarDescricaoEspanhol()
        {
            _rotacao++;   // avança a cada chamada — nunca fica estático

            // ── Traduz scores numéricos em intensidade verbal ─────────────────────────
            string IntensidadeFrente(float s) =>
                s > 8.5f ? "muy, muy cerca" :
                s > 7.0f ? "bastante cerca" :
                s > 5.5f ? "cerquita" : "un poco cerca";

            string IntensidadeLateral(float s) =>
                s > 8.0f ? "bloqueado" :
                s > 6.0f ? "muy apretado" :
                s > 4.0f ? "con cosas" : "algo ocupado";

            int PasitosDist(float s) =>
                Mathf.Clamp(Mathf.RoundToInt(10f - s), 1, 5);

            // Limiares
            bool frentePerigoso = dangerScore     > 6.0f;
            bool frenteAtencao  = dangerScore     > 3.5f;
            bool esqPerigoso    = leftZoneDanger  > 6.0f;
            bool dirPerigoso    = rightZoneDanger > 6.0f;
            bool esqAtencao     = leftZoneDanger  > 3.5f;
            bool dirAtencao     = rightZoneDanger > 3.5f;

            // ── CENÁRIO A: Tudo bloqueado ─────────────────────────────────────────────
            if (frentePerigoso && esqPerigoso && dirPerigoso)
            {
                string[] v = {
                    $"¡Uy, uy, uy! Hay cosas por todos lados. El frente está {IntensidadeFrente(dangerScore)} y los costados también. Paremos un momento.",
                    $"Todo el camino está ocupado. El frente a {PasitosDist(dangerScore)} pasitos, y los dos lados bloqueados. ¡Cuidado, cuidado!",
                    $"¡Ay! No podemos pasar ni por el frente ni por los lados. Mejor esperamos aquí un ratito."
                };
                return v[_rotacao % v.Length];
            }

            // ── CENÁRIO B: Objeto se aproximando rápido ───────────────────────────────
            if (absoluteVelocityAlert && frentePerigoso)
            {
                string[] v = {
                    $"¡Algo se mueve hacia nosotros por el frente! Está {IntensidadeFrente(dangerScore)}. ¡Para, para!",
                    $"¡Cuidado! Hay un obstáculo moviéndose {IntensidadeFrente(dangerScore)} al frente. ¡Detente ya!",
                    $"¡Rápido! Algo viene hacia acá por el frente. Está a solo {PasitosDist(dangerScore)} pasitos. ¡Alto!"
                };
                return v[_rotacao % v.Length];
            }

            // ── CENÁRIO C: Frente + esquerda bloqueados ───────────────────────────────
            if (frentePerigoso && esqPerigoso)
            {
                string[] v = {
                    $"El frente está {IntensidadeFrente(dangerScore)} y la izquierda {IntensidadeLateral(leftZoneDanger)}. Giremos hacia la derecha, que está más libre.",
                    $"No podemos ir recto ni a la izquierda. Por la derecha hay más espacio. ¡Vamos hacia allá!",
                    $"La izquierda y el frente están ocupados. La derecha es nuestro mejor camino ahora mismo."
                };
                return v[_rotacao % v.Length];
            }

            // ── CENÁRIO D: Frente + direita bloqueados ────────────────────────────────
            if (frentePerigoso && dirPerigoso)
            {
                string[] v = {
                    $"El frente está {IntensidadeFrente(dangerScore)} y la derecha {IntensidadeLateral(rightZoneDanger)}. Vamos hacia la izquierda que está más despejada.",
                    $"El frente y la derecha tienen obstáculos. La izquierda es el camino libre. ¡Por ahí vamos!",
                    $"No podemos ir recto ni a la derecha. Giremos suavecito hacia la izquierda."
                };
                return v[_rotacao % v.Length];
            }

            // ── CENÁRIO E: Só frente perigoso ─────────────────────────────────────────
            if (frentePerigoso)
            {
                string[] v = {
                    $"¡Uy! Hay un obstáculo {IntensidadeFrente(dangerScore)} al frente, a unos {PasitosDist(dangerScore)} pasitos. Hay que desviarse.",
                    $"El camino del frente está {IntensidadeFrente(dangerScore)} bloqueado. Busquemos por dónde pasar.",
                    $"¡Alto! Algo está {IntensidadeFrente(dangerScore)} enfrente nuestro. Peligro a {PasitosDist(dangerScore)} pasitos.",
                    $"Cuidadito, cuidadito. Hay algo {IntensidadeFrente(dangerScore)} delante de nosotros."
                };
                return v[_rotacao % v.Length];
            }

            // ── CENÁRIO F: Atenção moderada no frente + lado ──────────────────────────
            if (frenteAtencao && esqAtencao && !dirAtencao)
            {
                string[] v = {
                    $"Hay cosas {IntensidadeFrente(dangerScore)} al frente y {IntensidadeLateral(leftZoneDanger)} a la izquierda. La derecha está más libre.",
                    $"El frente y la izquierda están {IntensidadeFrente(dangerScore)}. Mejor nos corremos un poquito hacia la derecha.",
                };
                return v[_rotacao % v.Length];
            }

            if (frenteAtencao && dirAtencao && !esqAtencao)
            {
                string[] v = {
                    $"El frente está {IntensidadeFrente(dangerScore)} y la derecha {IntensidadeLateral(rightZoneDanger)}. Hacia la izquierda hay más espacio.",
                    $"Hay cosas cerca al frente y a la derecha. Nos movemos un poquito hacia la izquierda.",
                };
                return v[_rotacao % v.Length];
            }

            if (frenteAtencao)
            {
                string[] v = {
                    $"Hay algo {IntensidadeFrente(dangerScore)} al frente, a unos {PasitosDist(dangerScore)} pasitos. Podemos pasar con cuidado.",
                    $"El frente tiene un obstáculo {IntensidadeFrente(dangerScore)}, pero hay espacio. Con cuidadito podemos.",
                    $"Atencionciito, hay algo al frente. No tan cerca todavía, pero vigila.",
                };
                return v[_rotacao % v.Length];
            }

            // ── CENÁRIO G: Laterais com problema, centro livre ────────────────────────
            if (esqPerigoso && !dirAtencao)
            {
                string[] v = {
                    $"La izquierda está {IntensidadeLateral(leftZoneDanger)}, pero el camino por la derecha está libre. ¡Vamos!",
                    $"Cuidado con la izquierda, que está {IntensidadeLateral(leftZoneDanger)}. Por la derecha podemos pasar bien.",
                };
                return v[_rotacao % v.Length];
            }

            if (dirPerigoso && !esqAtencao)
            {
                string[] v = {
                    $"La derecha está {IntensidadeLateral(rightZoneDanger)}, pero por la izquierda el camino está despejado. ¡Adelante!",
                    $"Hay algo {IntensidadeLateral(rightZoneDanger)} a la derecha. La izquierda está libre, por ahí vamos.",
                };
                return v[_rotacao % v.Length];
            }

            if (esqAtencao && dirAtencao)
            {
                string[] v = {
                    $"Los costados tienen cosas cerca, la izquierda {IntensidadeLateral(leftZoneDanger)} y la derecha {IntensidadeLateral(rightZoneDanger)}, pero el frente está libre.",
                    $"A los dos lados hay obstáculos, pero el camino del frente está despejado. Sigamos de frente.",
                };
                return v[_rotacao % v.Length];
            }

            // ── CENÁRIO H: Movimento detectado, caminho livre ─────────────────────────
            if (absoluteVelocityAlert)
            {
                string[] v = {
                    "El camino parece libre, pero algo se está moviendo cerca. Sigamos con calma.",
                    "Hay movimiento por aquí, pero el camino está despejado. Atención y adelante.",
                };
                return v[_rotacao % v.Length];
            }

            // ── CENÁRIO I: Caminho completamente livre ────────────────────────────────
            {
                string[] v = {
                    "El camino está despejado. Podemos caminar tranquilos hacia adelante.",
                    "¡Todo libre! Sigamos adelante sin problemas.",
                    "El camino está libre. ¡Vamos con confianza!",
                    "No hay obstáculos. El camino es nuestro, ¡adelante!",
                };
                return v[_rotacao % v.Length];
            }
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
    // CORRECÇÃO: MiDaS retorna PROFUNDIDADE INVERSA:
    //   valor alto  = pixel LONGE   = sem perigo
    //   valor baixo = pixel PERTO   = PERIGO
    // Portanto perigo = 1.0 - (depth / maxDepth)
    // ============================================================
    [BurstCompile]
    struct MidasZoneJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<float> depthArray;
        [ReadOnly]  public NativeArray<float> maxDepth;  // lê maxDepthOut[0]
        public int tensorSize;

        // Quando 1: troca as zonas Esq <-> Dir (correção de espelho da câmara)
        public int inverterEixoX; // 0=normal, 1=invertido

        // Acumuladores por linha (index = linha Y)
        [WriteOnly] public NativeArray<float> linhaEsquerda;
        [WriteOnly] public NativeArray<float> linhaDireita;
        [WriteOnly] public NativeArray<float> linhaCentral;

        [WriteOnly] public NativeArray<int> countEsq;
        [WriteOnly] public NativeArray<int> countDir;
        [WriteOnly] public NativeArray<int> countCen;

        public void Execute(int y)
        {
            float somaE = 0f, somaD = 0f, somaC = 0f;
            int cE = 0, cD = 0, cC = 0;

            float maxD = maxDepth[0];
            if (maxD < 0.0001f) maxD = 0.0001f;
            int terco = tensorSize / 3;
            // Zona frontal: apenas metade inferior da imagem (evita teto)
            bool linhaFrontal = y > tensorSize / 2;

            for (int x = 0; x < tensorSize; x++)
            {
                float normalizado = depthArray[y * tensorSize + x] / maxD;
                if (normalizado > 1f) normalizado = 1f;

                // Perigo = inverso da profundidade:
                // perto (low depth) -> alto perigo | longe (high depth) -> baixo perigo
                float perigo = 1f - normalizado;

                // Aplica espelho de eixo X se necessário
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

    public class MidasInferenceManager : MonoBehaviour
    {
        [Header("Rede Neural MiDaS")]
        public ModelAsset modelAsset;

        [Header("Configurações Físicas")]
        public float velocityThreshold = 2.5f;

        [Header("Calibração de Câmara")]
        [Tooltip(
            "Inverte os eixos Esquerda/Direita do mapa de profundidade.\n" +
            "Ative se os comandos de evasão estiverem trocados (direita/esquerda invertidas).\n" +
            "Causa comum: óculos com câmara espelhada horizontalmente."
        )]
        public bool inverterEixoX = false;

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
                inverterEixoX = inverterEixoX ? 1 : 0,
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