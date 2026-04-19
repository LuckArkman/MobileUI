using UnityEngine;
using UnityEngine.UI;
using LuckArkman.XR.Navigation;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Networking;
using TMPro; 

namespace LuckArkman.XR.UI
{
    public class PrototypeUIManager : MonoBehaviour
    {
        [Header("Gerenciadores")]
        public RouteManager routeManager;
        
        [Header("Telas (Painéis)")]
        public GameObject panelMainMenu;
        public GameObject panelRecording;
        public GameObject panelNavigation;
        public GameObject panelResults;

        [Header("Textos de Contagem")]
        public TextMeshProUGUI txtRecordingNodes; // Mostra "Pontos Gravados: X"
        public TextMeshProUGUI txtNavDistance;
        
        [Header("Textos de Feedback Temporário (Local)")]
        [Tooltip("Texto perto dos botões do Menu Principal")]
        public TextMeshProUGUI txtFeedbackMenu; 
        [Tooltip("Texto perto do botão 'Punto de Ajuste'")]
        public TextMeshProUGUI txtFeedbackPonto; 

        [Header("Campos de Resposta (Dashboard)")]
        public TextMeshProUGUI txtRes_FPS;
        public TextMeshProUGUI txtRes_Tempo;
        public TextMeshProUGUI txtRes_NoLento;

        [Header("Configurações ESP32")]
        public string esp32IpAddress = "192.168.17.102";

        // --- Variáveis de Lógica ---
        private RouteData rotaSendoGravada;
        private bool isNavigating = false;

        // --- Variáveis de Métricas (Dashboard) ---
        private float startTime;
        private float nodeStartTime;
        private List<float> nodeDurations = new List<float>();
        private int totalFramesProcessed;

        void Start()
        {
            ShowPanel(panelMainMenu);
            
            // Limpa os textos de feedback no início
            if (txtFeedbackMenu != null) txtFeedbackMenu.text = "";
            if (txtFeedbackPonto != null) txtFeedbackPonto.text = "";
        }

        void Update()
        {
            if (isNavigating) totalFramesProcessed++;
        }

        // ==========================================
        // SISTEMA DE FEEDBACK TEMPORÁRIO
        // ==========================================
        private void MostrarFeedbackMenu(string mensagem)
        {
            if (txtFeedbackMenu != null)
            {
                txtFeedbackMenu.text = mensagem;
                StopCoroutine("LimparFeedbackMenu");
                StartCoroutine("LimparFeedbackMenu");
            }
        }

        private IEnumerator LimparFeedbackMenu()
        {
            yield return new WaitForSeconds(2.5f);
            if (txtFeedbackMenu != null) txtFeedbackMenu.text = "";
        }

        private void MostrarFeedbackPonto(string mensagem)
        {
            if (txtFeedbackPonto != null)
            {
                txtFeedbackPonto.text = mensagem;
                StopCoroutine("LimparFeedbackPonto");
                StartCoroutine("LimparFeedbackPonto");
            }
        }

        private IEnumerator LimparFeedbackPonto()
        {
            yield return new WaitForSeconds(2.0f);
            if (txtFeedbackPonto != null) txtFeedbackPonto.text = "";
        }

        // ==========================================
        // MODO CRIAÇÃO (GRAVAÇÃO)
        // ==========================================
        public void Btn_IniciarGravacao()
        {
            rotaSendoGravada = new RouteData("Rota Teste");
            if (txtRecordingNodes != null) txtRecordingNodes.text = "Pontos Gravados: 0";
            if (txtFeedbackPonto != null) txtFeedbackPonto.text = ""; // Limpa feedback antigo
            
            ShowPanel(panelRecording);
        }

        public void Btn_GravarPontoGPS()
        {
            if (GPSManager.Instance != null && GPSManager.Instance.gpsAtivo)
            {
                double latAtual = GPSManager.Instance.latitude;
                double lonAtual = GPSManager.Instance.longitude;
                rotaSendoGravada.nos.Add(new RouteNode(latAtual, lonAtual));
                if (txtRecordingNodes != null) txtRecordingNodes.text = $"Pontos Gravados: {rotaSendoGravada.nos.Count}";
            }
            else 
            {
                double latAtual = -14.5366 + (Random.Range(-0.0001f, 0.0001f)); 
                double lonAtual = -49.1419 + (Random.Range(-0.0001f, 0.0001f));
                rotaSendoGravada.nos.Add(new RouteNode(latAtual, lonAtual));
                if (txtRecordingNodes != null) txtRecordingNodes.text = $"Pontos Gravados: {rotaSendoGravada.nos.Count} (Simulado)";
            }

            // Exibe o feedback exatamente no botão de gravar ponto!
            MostrarFeedbackPonto("Ponto Registrado!");
        }

        public void Btn_FinalizarSalvarRota()
        {
            if (rotaSendoGravada.nos.Count > 0)
            {
                routeManager.database.savedRoutes.Clear(); 
                routeManager.AddNewRoute(rotaSendoGravada);
                
                ShowPanel(panelMainMenu);
                // Exibe no menu principal que a gravação deu certo
                MostrarFeedbackMenu("<color=green>Rota Guardada com Sucesso!</color>");
            }
            else
            {
                ShowPanel(panelMainMenu);
                MostrarFeedbackMenu("<color=yellow>Gravação Cancelada: Nenhum ponto salvo.</color>");
            }
        }

        // ==========================================
        // MODO NAVEGAÇÃO
        // ==========================================
        public void Btn_IniciarNavegacao()
        {
            if (routeManager.database.savedRoutes.Count == 0)
            {
                // Dá o aviso de erro logo abaixo do botão no Menu
                MostrarFeedbackMenu("<color=red>Erro: Nenhuma rota gravada ainda!</color>");
                return;
            }

            RouteData rotaAtiva = routeManager.database.savedRoutes[0];
            
            startTime = Time.time;
            nodeStartTime = Time.time;
            nodeDurations.Clear();
            totalFramesProcessed = 0;
            isNavigating = true;

            if (NavigationManager.Instance != null)
                NavigationManager.Instance.IniciarRota(rotaAtiva);

            ShowPanel(panelNavigation);
        }

        public void RegistrarPontoAlcancado()
        {
            nodeDurations.Add(Time.time - nodeStartTime);
            nodeStartTime = Time.time;
        }

        public void Btn_Finish()
        {
            isNavigating = false;
            StartCoroutine(SendServoReset());

            float tempoTotal = Time.time - startTime;
            float mediaFPS = tempoTotal > 0 ? (totalFramesProcessed / tempoTotal) : 0;

            float maiorTempo = 0;
            int indexPonto = 0;
            for (int i = 0; i < nodeDurations.Count; i++)
            {
                if (nodeDurations[i] > maiorTempo)
                {
                    maiorTempo = nodeDurations[i];
                    indexPonto = i + 1;
                }
            }

            if(txtRes_FPS != null) txtRes_FPS.text = mediaFPS.ToString("F1");
            if(txtRes_Tempo != null) txtRes_Tempo.text = tempoTotal.ToString("F1") + " s";
            if(txtRes_NoLento != null) txtRes_NoLento.text = indexPonto.ToString();

            ShowPanel(panelResults);
        }

        private IEnumerator SendServoReset()
        {
            string url = $"http://{esp32IpAddress}:81/actuator?angle=90";
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 1;
                yield return request.SendWebRequest();
            }
        }

        public void Btn_VoltarAoMenu()
        {
            ShowPanel(panelMainMenu);
        }

        private void ShowPanel(GameObject panelToShow)
        {
            panelMainMenu.SetActive(false);
            panelRecording.SetActive(false);
            panelNavigation.SetActive(false);
            panelResults.SetActive(false);

            panelToShow.SetActive(true);
        }
    }
}