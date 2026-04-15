using UnityEngine;
using System.Diagnostics;

namespace LuckArkman.XR.Networking
{
    /// <summary>
    /// Monitor de latência para medir RTT e tempo de processamento de vídeo.
    /// </summary>
    public class LatencyMonitor : MonoBehaviour
    {
        private Stopwatch stopwatch;
        private float lastRtt = 0f;
        private float videoLatency = 0f;

        [Header("Configurações de Alerta")]
        public float latencyThresholdMs = 50f;

        public float LastRtt => lastRtt;
        public float VideoLatency => videoLatency;

        private void Start()
        {
            stopwatch = new Stopwatch();
        }

        /// <summary>
        /// Inicia a medição de um Ping.
        /// </summary>
        public void StartTiming()
        {
            stopwatch.Restart();
        }

        /// <summary>
        /// Finaliza a medição ao receber a resposta do servidor.
        /// </summary>
        public void EndTiming()
        {
            stopwatch.Stop();
            lastRtt = (float)stopwatch.Elapsed.TotalMilliseconds;
            
            if (lastRtt > latencyThresholdMs)
            {
                UnityEngine.Debug.LogWarning($"[Latency] Latência acima do esperado: {lastRtt:F2}ms");
            }
        }

        /// <summary>
        /// Registra a latência de processamento de um frame de vídeo específico.
        /// </summary>
        public void UpdateVideoLatency(float latencyMs)
        {
            videoLatency = latencyMs;
        }

        // Simulação para o HUD (enquanto não temos pacotes reais)
        private void Update()
        {
            // Em aplicação real, isso seria disparado por eventos de rede
            if (Time.frameCount % 60 == 0)
            {
                // Simulando variação de rede
                float jitter = UnityEngine.Random.Range(-5f, 5f);
                lastRtt = Mathf.Clamp(20f + jitter, 10f, 100f);
            }
        }
    }
}
