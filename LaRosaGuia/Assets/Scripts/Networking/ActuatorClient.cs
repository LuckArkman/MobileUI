using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

namespace LuckArkman.XR.Networking
{
    public class ActuatorClient : MonoBehaviour
    {
        private string targetIp = "";
        private float lastSendTime = 0f;
        private float cooldownPeriod = 1.0f; // Espera 1 segundo entre comandos para não travar o Wi-Fi

        public void SetTargetIp(string ip)
        {
            targetIp = ip;
        }

        // --- NOVO: FEEDBACK POSITIVO DE CONEXÃO ---
        public void SendConnectionSuccess()
        {
            if (string.IsNullOrEmpty(targetIp)) return;
            // Manda o servo para o centro e emite um bipe agudo de sucesso
            string url = $"http://{targetIp}:81/actuator?angle=90&buzz=3000";
            StartCoroutine(PostCommand(url));
        }

        public void SendActuatorCommand(int angle)
        {
            if (string.IsNullOrEmpty(targetIp)) return;

            // Substituímos a trava de "ângulo repetido" por um Cronômetro.
            // Se o perigo continuar, ele bipa a cada 1 segundo.
            if (Time.time - lastSendTime < cooldownPeriod) return;
            
            lastSendTime = Time.time;
            
            string url = $"http://{targetIp}:81/actuator?angle={angle}&buzz=1500";
            StartCoroutine(PostCommand(url));
        }

        private IEnumerator PostCommand(string url)
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                webRequest.timeout = 2; 
                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[ActuatorClient] Falha na placa: {webRequest.error}");
                }
                else
                {
                    Debug.Log($"[ActuatorClient] Comando Físico Executado: {url}");
                }
            }
        }
    }
}