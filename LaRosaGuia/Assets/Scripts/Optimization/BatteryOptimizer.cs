using UnityEngine;

namespace LuckArkman.XR.Optimization
{
    /// <summary>
    /// Gerencia a performance do dispositivo Android para maximizar a duração da bateria.
    /// Ajusta a taxa de atualização e brilho baseado no estado do sistema.
    /// </summary>
    public class BatteryOptimizer : MonoBehaviour
    {
        [Header("Configurações de Energia")]
        public int targetFrameRateActive = 60;
        public int targetFrameRateIdle = 30;
        
        private void Start()
        {
            // Otimização inicial para mobile
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = targetFrameRateActive;
            
            // Sugestão para o Android: Reduzir a frequência da CPU quando possível via API nativa (requer plugin)
            Debug.Log("[BatteryOptimizer] Sistema de economia de energia inicializado.");
        }

        public void SetLowPowerMode(bool enabled)
        {
            Application.targetFrameRate = enabled ? targetFrameRateIdle : targetFrameRateActive;
            Debug.Log($"[BatteryOptimizer] Low Power Mode: {enabled} (FPS: {Application.targetFrameRate})");
        }

        private void Update()
        {
            // Monitoramento simples de nível de bateria
            if (SystemInfo.batteryLevel < 0.15f && SystemInfo.batteryStatus == BatteryStatus.Discharging)
            {
                SetLowPowerMode(true);
            }
        }
    }
}
