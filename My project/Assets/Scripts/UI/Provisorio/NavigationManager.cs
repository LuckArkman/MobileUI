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
        public string esp32IpAddress = "192.168.17.102";
        public int portaControle = 81;

        [Header("Interface (UI)")]
        public TextMeshProUGUI txtDistanciaUI;
        public PrototypeUIManager uiManager; 

        [Header("Parâmetros de Navegação")]
        public float raioDeCaptura = 4.0f; 
        private float servoUpdateInterval = 0.5f; 

        [Header("Comunicação com IA (Decision)")]
        [Tooltip("O módulo de Decisão vai ler este valor em tempo real.")]
        public int anguloDesejadoGPS = 90; // Começa olhando para frente
        // --- Estado Interno ---
        private RouteData rotaAtiva;
        private int indicePontoAtual = 0;
        private bool isNavigating = false;
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

        
        // NOVO: Expor o ângulo exato (-180 a 180) para a IA saber se o destino está nas costas
        [HideInInspector] public float anguloRelativoAoDestino = 0f; 

        void Update()
        {
            if (!isNavigating || !GPSManager.Instance.gpsAtivo || rotaAtiva == null) return;

            float latAtual = GPSManager.Instance.latitude;
            float lonAtual = GPSManager.Instance.longitude;
            float headingAtual = GPSManager.Instance.currentHeading;

            // =====================================================================
            // NOVO: SISTEMA DE NÃO-REGRESSÃO E PRIORIDADE DE ID MAIOR
            // Escaneia a rota de trás para frente. Se achar um ponto no raio, pula direto pra ele.
            // =====================================================================
            for (int i = rotaAtiva.nos.Count - 1; i >= indicePontoAtual; i--)
            {
                RouteNode checkNode = rotaAtiva.nos[i];
                float checkDist = CalculateDistance(latAtual, lonAtual, (float)checkNode.latitude, (float)checkNode.longitude);
                
                if (checkDist <= raioDeCaptura)
                {
                    indicePontoAtual = i; // Pula todos os pontos anteriores (Não-Regressão)
                    AvancarParaProximoPonto();
                    return; // Encerra o frame, pois o índice mudou
                }
            }

            // Se não alcançou nenhum ponto, foca no ponto alvo atual
            RouteNode alvo = rotaAtiva.nos[indicePontoAtual];
            float distancia = CalculateDistance(latAtual, lonAtual, (float)alvo.latitude, (float)alvo.longitude);
            float anguloDestino = CalculateBearing(latAtual, lonAtual, (float)alvo.latitude, (float)alvo.longitude);

            // 4. Atualiza a Tela (Apenas os números, pois a arte já tem a palavra)
            if (txtDistanciaUI != null) 
            {
                txtDistanciaUI.text = $"{distancia:F1} m";
            }

            // Cálculo do ângulo
            anguloRelativoAoDestino = Mathf.DeltaAngle(headingAtual, anguloDestino);
            int anguloServo = Mathf.Clamp(Mathf.RoundToInt(90f + anguloRelativoAoDestino), 0, 180);

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
            // O reset manual ainda manda direto por segurança
            StartCoroutine(SendServoCommand(90));
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

        // --- COMUNICAÇÃO HTTP ---
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