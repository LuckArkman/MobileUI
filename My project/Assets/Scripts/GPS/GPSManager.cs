using UnityEngine;
using System.Collections;

namespace LuckArkman.XR.Navigation
{
    public class GPSManager : MonoBehaviour
    {
        // Singleton: Permite que outros scripts achem este facilmente usando GPSManager.Instance
        public static GPSManager Instance { get; private set; }

        [Header("Dados em Tempo Real")]
        public float latitude;
        public float longitude;
        public float currentHeading; // Para onde o celular está apontando (Bússola)
        public bool gpsAtivo = false;

        void Awake()
        {
            // Garante que só exista UM GPSManager na cena
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
            }
            else
            {
                Instance = this;
            }
        }

        void Start()
        {
            StartCoroutine(IniciarSensores());
        }

        private IEnumerator IniciarSensores()
        {
            // 1. Verifica se o usuário permitiu o uso de localização no celular
            if (!Input.location.isEnabledByUser)
            {
                Debug.LogWarning("[GPS] Localização desativada nas configurações do Android/iOS.");
                yield break;
            }

            // 2. Inicia o serviço de GPS (Precisão de 1 metro, atualiza a cada 1 metro de caminhada)
            Input.location.Start(1f, 1f);
            
            // 3. Inicia a Bússola magnética
            Input.compass.enabled = true;

            // 4. Aguarda o celular conectar aos satélites (Timeout de 20 segundos)
            int maxWait = 20;
            while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                yield return new WaitForSeconds(1);
                maxWait--;
            }

            // 5. Se o tempo estourou ou o GPS falhou
            if (maxWait < 1 || Input.location.status == LocationServiceStatus.Failed)
            {
                Debug.LogError("[GPS] Falha ao conectar com os satélites.");
                yield break;
            }

            // 6. Sucesso Total!
            gpsAtivo = true;
            Debug.Log("[GPS] Satélites e Bússola conectados com sucesso!");
        }

        void Update()
        {
            // Se o GPS está rodando perfeitamente, atualiza as variáveis a cada frame
            if (gpsAtivo && Input.location.status == LocationServiceStatus.Running)
            {
                latitude = Input.location.lastData.latitude;
                longitude = Input.location.lastData.longitude;
                currentHeading = Input.compass.trueHeading; // Vai de 0 a 360 graus
            }
        }
    }
}