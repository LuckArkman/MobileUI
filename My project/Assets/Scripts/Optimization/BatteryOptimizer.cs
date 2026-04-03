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
            // Garante que a tela não bloqueie automaticamente durante a navegação.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // Permite que Update() e Coroutines continuem rodando quando o app
            // está em background (complementa o BackgroundServiceManager).
            Application.runInBackground = true;

            Application.targetFrameRate = targetFrameRateActive;

            Debug.Log("[BatteryOptimizer] Sistema de economia de energia inicializado.");
        }

        public void SetLowPowerMode(bool isLowPower)
        {
            Application.targetFrameRate = isLowPower ? targetFrameRateIdle : targetFrameRateActive;
            Debug.Log($"[BatteryOptimizer] Low Power Mode: {isLowPower} (FPS: {Application.targetFrameRate})");
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
