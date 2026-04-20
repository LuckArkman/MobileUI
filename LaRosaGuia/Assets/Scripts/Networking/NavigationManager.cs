using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Networking;
using LuckArkman.XR.UI;
using TMPro;

namespace LuckArkman.XR.Navigation
{
    public class NavigationManager : MonoBehaviour
    {
        public static NavigationManager Instance { get; private set; }

        [Header("Configurações do ESP32")]
        // IP Atualizado conforme solicitado
        public string esp32IpAddress = "192.168.43.50";
        public int portaControle = 81;

        [Header("Interface (UI)")]
        public TextMeshProUGUI txtDistanciaUI;
        public PrototypeUIManager uiManager; 

        [Header("Parâmetros de Navegação")]
        public float raioDeCapturaBase = 4.0f; 
        private float servoUpdateInterval = 0.5f; 

        [Header("Comunicação com IA (Decision)")]
        [Tooltip("O módulo de Decisão vai ler este valor em tempo real.")]
        public int anguloDesejadoGPS = 90; 
        
        // --- Estado Interno ---
        private RouteData rotaAtiva;
        private int indicePontoAtual = 0;
        public bool isNavigating = false; 
        private float lastServoUpdateTime = 0f;

        void Awake()
        {
            if (Instance != null && Instance != this) Destroy(this.gameObject);
            else Instance = this;
        }

        public void IniciarRota(RouteData novaRota)
        {
            if (novaRota == null || novaRota.nos.Count == 0) return;

            rotaAtiva = novaRota;
            indicePontoAtual = 0;
            isNavigating = true;
            Debug.Log($"[Navigation] Rota Iniciada! Total de Pontos: {rotaAtiva.nos.Count}");
        }

        public void PararNavegacao()
        {
            isNavigating = false;
            rotaAtiva = null;
            anguloRelativoAoDestino = 0f;
            Debug.Log("[Navigation] Navegação Cancelada ou Pausada.");
        }

        [HideInInspector] public float anguloRelativoAoDestino = 0f;

        /// <summary>Distância em metros até o ponto GPS atual. Consumido por RouteProgressTracker.</summary>
        [HideInInspector] public float distanciaAoDestino = 0f;

        void Update()
        {
            if (!isNavigating || !GPSManager.Instance.gpsAtivo || rotaAtiva == null) return;

            float latAtual = GPSManager.Instance.latitude;
            float lonAtual = GPSManager.Instance.longitude;
            float headingAtual = GPSManager.Instance.currentHeading;

            float velocidadeEstimada = Input.compass.enabled ? Input.compass.headingAccuracy : 0; 
            float raioDeCaptura = raioDeCapturaBase + (velocidadeEstimada * 0.1f);

            // 1. CHECAGEM DE PROGRESSO
            RouteNode alvoAtual = rotaAtiva.nos[indicePontoAtual];
            float distanciaAteAtual = CalculateDistance(latAtual, lonAtual, (float)alvoAtual.latitude, (float)alvoAtual.longitude);
            distanciaAoDestino = distanciaAteAtual; 

            // 2. AVANÇO ORGÂNICO
            if (distanciaAteAtual <= raioDeCaptura)
            {
                AvancarParaProximoPonto();
                return; 
            }

            // 3. LOOKAHEAD PROGRESSIVO
            float anguloDestino = CalculateBearing(latAtual, lonAtual, (float)alvoAtual.latitude, (float)alvoAtual.longitude);

            if (distanciaAteAtual < raioDeCaptura * 2f && indicePontoAtual + 1 < rotaAtiva.nos.Count)
            {
                RouteNode proximoAlvo = rotaAtiva.nos[indicePontoAtual + 1];
                float anguloProximo = CalculateBearing(latAtual, lonAtual, (float)proximoAlvo.latitude, (float)proximoAlvo.longitude);
                
                float fatorMescla = Mathf.Clamp01(1f - (distanciaAteAtual / (raioDeCaptura * 2f)));
                anguloDestino = Mathf.LerpAngle(anguloDestino, anguloProximo, fatorMescla);
            }

            // 4. ATUALIZA A UI
            if (txtDistanciaUI != null) 
            {
                int passosNavegacao = Mathf.Max(1, Mathf.RoundToInt(distanciaAteAtual / 0.75f));
                txtDistanciaUI.text = $"{passosNavegacao} passos";
            }

            // 5. CÁLCULO DE BÚSSOLA COM HISTERESE
            float anguloBruto = Mathf.DeltaAngle(headingAtual, anguloDestino);
            
            if (Mathf.Abs(anguloBruto) < 20f)
            {
                anguloRelativoAoDestino = 0f; 
            }
            else
            {
                anguloRelativoAoDestino = anguloBruto;
            }

            int anguloServo = Mathf.Clamp(Mathf.RoundToInt(90f + anguloRelativoAoDestino), 0, 180);

            // 6. ATUALIZA O SERVO
            if (Time.time - lastServoUpdateTime > servoUpdateInterval)
            {
                anguloDesejadoGPS = anguloServo;
                lastServoUpdateTime = Time.time;
            }
        }

        private void AvancarParaProximoPonto()
        {
            Debug.Log($"[Navigation] Ponto {indicePontoAtual + 1} alcançado!");
            if (uiManager != null) uiManager.RegistrarPontoAlcancado();
            
            indicePontoAtual++;

            var progressManager = Object.FindFirstObjectByType<LuckArkman.XR.Main.RouteProgressManager>();
            if (progressManager != null)
            {
                progressManager.AvancarParaProximoCheckPoint();
            }

            if (indicePontoAtual >= rotaAtiva.nos.Count)
            {
                Debug.Log("[Navigation] Destino Final Alcançado!");
                isNavigating = false;
                if (txtDistanciaUI != null) txtDistanciaUI.text = "CHEGOU!";
                if (uiManager != null) uiManager.Btn_Finish(); 
            }
        }

        public void PularPontoAtual()
        {
            if (isNavigating) AvancarParaProximoPonto();
        }

        public void ResetarServoManual()
        {
            StartCoroutine(SendServoCommand(90));
        }

        // ==========================================================
        // NOVO: Função para sortear e girar o servo aleatoriamente
        // ==========================================================
        public void GirarServoAleatorio()
        {
            int anguloAleatorio = UnityEngine.Random.Range(0, 181);
            Debug.Log($"[Navigation] Girando servo aleatoriamente para: {anguloAleatorio}°");
            StartCoroutine(SendServoCommand(anguloAleatorio));
        }

        // --- FÓRMULAS MATEMÁTICAS ---
        private float CalculateBearing(float lat1, float lon1, float lat2, float lon2)
        {
            float rLat1 = lat1 * Mathf.Deg2Rad;
            float rLat2 = lat2 * Mathf.Deg2Rad;
            float dLon = (lon2 - lon1) * Mathf.Deg2Rad;

            float y = Mathf.Sin(dLon) * Mathf.Cos(rLat2);
            float x = Mathf.Cos(rLat1) * Mathf.Sin(rLat2) - Mathf.Sin(rLat1) * Mathf.Cos(rLat2) * Mathf.Cos(dLon);

            float bearing = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            return (bearing + 360f) % 360f; 
        }

        private float CalculateDistance(float lat1, float lon1, float lat2, float lon2)
        {
            float R = 6371000f; 
            float rLat1 = lat1 * Mathf.Deg2Rad;
            float rLat2 = lat2 * Mathf.Deg2Rad;
            float dLat = (lat2 - lat1) * Mathf.Deg2Rad;
            float dLon = (lon2 - lon1) * Mathf.Deg2Rad;

            float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2) +
                      Mathf.Cos(rLat1) * Mathf.Cos(rLat2) * Mathf.Sin(dLon / 2) * Mathf.Sin(dLon / 2);
            float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
            return R * c; 
        }

        private IEnumerator SendServoCommand(int angle)
        {
            string url = $"http://{esp32IpAddress}:{portaControle}/actuator?angle={angle}";
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 1;
                yield return request.SendWebRequest();
            }
        }
    }
}