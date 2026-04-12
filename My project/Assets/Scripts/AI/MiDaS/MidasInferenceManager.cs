using UnityEngine;
using Unity.InferenceEngine; 
using System;

namespace LuckArkman.XR.AI
{
    public struct MidasResult
    {
        public float dangerScore; 
        public bool absoluteVelocityAlert; 
        public float leftZoneDanger; 
        public float rightZoneDanger; 
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
                // Debug.Log("[MiDaS] Motor InferenceEngine GPU iniciado com sucesso!"); // -> COMENTADO PARA LIMPAR O CONSOLE
            }
        }

        public MidasResult ExecuteInference(Texture2D cameraImage)
        {
            MidasResult result = new MidasResult();
            if (engineWorker == null || cameraImage == null) return result;

            // 1. CRIA O TENSOR
            TensorShape shape = new TensorShape(1, 3, TENSOR_SIZE, TENSOR_SIZE);
            Tensor<float> inputTensor = new Tensor<float>(shape);
            
            // 2. CONVERTE A IMAGEM
            TextureTransform transform = new TextureTransform();
            TextureConverter.ToTensor(cameraImage, inputTensor, transform);

            // 3. RODA A IA
            engineWorker.Schedule(inputTensor);

            // 4. PEGA O RESULTADO
            Tensor<float> outputTensor = engineWorker.PeekOutput() as Tensor<float>;
            
            // NOVO: Proteção contra erro de cache da GPU
            if (outputTensor == null) 
            {
                inputTensor.Dispose();
                return result;
            }

            float[] depthArray = outputTensor.DownloadToArray();

            // 5. LIBERA A MEMÓRIA DA IMAGEM
            inputTensor.Dispose();

            // 6. PROCESSA AS ZONAS FÍSICAS E MATEMÁTICA
            ProcessarZonasDeRisco(depthArray, ref result);

            float deltaTime = Time.time - lastProcessTime;
            if (deltaTime > 0)
            {
                float depthDelta = (result.dangerScore - previousGlobalDepth) / deltaTime;
                result.absoluteVelocityAlert = depthDelta > velocityThreshold;
            }

            previousGlobalDepth = result.dangerScore;
            lastProcessTime = Time.time;

            // =========================================================================================
            // NOVO LOG: Avisa sobre o bloqueio espacial apenas se houver um risco moderado/alto (> 4.0)
            // =========================================================================================
            if (result.dangerScore >= 4.0f || result.absoluteVelocityAlert)
            {
                // Formata a mensagem como um painel de radar para fácil leitura
                Debug.Log($"[MiDaS Radar] Frente: {result.dangerScore:F1}/10 | Esq: {result.leftZoneDanger:F1}/10 | Dir: {result.rightZoneDanger:F1}/10 | Alerta Movimento: {(result.absoluteVelocityAlert ? "SIM" : "NÃO")}");
            }

            return result;
        }

        private void ProcessarZonasDeRisco(float[] depthArray, ref MidasResult result)
        {
            float somaEsquerda = 0f, somaDireita = 0f, somaCentralBaixo = 0f;
            int countEsquerda = 0, countDireita = 0, countCentralBaixo = 0;

            float maxDepthRaw = 0.0001f; 
            for (int i = 0; i < depthArray.Length; i++)
            {
                if (depthArray[i] > maxDepthRaw) maxDepthRaw = depthArray[i];
            }

            for (int y = 0; y < TENSOR_SIZE; y++)
            {
                for (int x = 0; x < TENSOR_SIZE; x++)
                {
                    int index = y * TENSOR_SIZE + x;
                    float pixelDepth = Mathf.Clamp01(depthArray[index] / maxDepthRaw);

                    if (x < TENSOR_SIZE / 3) 
                    {
                        somaEsquerda += pixelDepth;
                        countEsquerda++;
                    }
                    else if (x > (TENSOR_SIZE / 3) * 2) 
                    {
                        somaDireita += pixelDepth;
                        countDireita++;
                    }

                    if (x >= TENSOR_SIZE / 3 && x <= (TENSOR_SIZE / 3) * 2 && y > TENSOR_SIZE / 2)
                    {
                        somaCentralBaixo += pixelDepth;
                        countCentralBaixo++;
                    }
                }
            }

            result.leftZoneDanger = countEsquerda > 0 ? (somaEsquerda / countEsquerda) * 10f : 0;
            result.rightZoneDanger = countDireita > 0 ? (somaDireita / countDireita) * 10f : 0;
            result.dangerScore = countCentralBaixo > 0 ? (somaCentralBaixo / countCentralBaixo) * 10f : 0;
        }

        private void OnDestroy()
        {
            engineWorker?.Dispose();
        }
    }
}