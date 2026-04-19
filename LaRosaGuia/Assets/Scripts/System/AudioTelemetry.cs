using UnityEngine;

namespace LuckArkman.XR.System
{
    /// <summary>
    /// Sistema Provisório de Telemetria de Áudio (Debug por Áudio).
    /// Associa sons a estados de erro para facilitar testes em campo sem precisar olhar para o console.
    /// </summary>
    public class AudioTelemetry : MonoBehaviour
    {
        public static AudioTelemetry Instance { get; private set; }

        [Header("Clipes de Telemetria")]
        [Tooltip("Som de 'Mola' ou 'Clique Vazio': Erros de Inspector (Transform não linkado, NullReference).")]
        public AudioClip inspectorErrorClip;
        
        [Tooltip("Bipe leve (Gota d'água): Alertas médios (GPS driftou, IA pulou frame).")]
        public AudioClip mediumWarningClip;
        
        [Tooltip("Bipe duplo agudo: Erros de lógica ou conexão (latência alta, rede, hardware).")]
        public AudioClip logicErrorClip;
        
        [Tooltip("Buzzer grave: Exceptions fatais ou perda total do tracking.")]
        public AudioClip fatalErrorClip;

        private AudioSource audioSource;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // Persistir pelas cenas de teste
                
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f; // Som 2D, volume constante independentemente da posição
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void PlayInspectorError()
        {
            if (inspectorErrorClip != null && audioSource != null) 
                audioSource.PlayOneShot(inspectorErrorClip);
            
            Debug.LogWarning("[AudioTelemetry] 🟡 Inspector/Null Error triggered.");
        }

        public void PlayMediumWarning()
        {
            if (mediumWarningClip != null && audioSource != null) 
                audioSource.PlayOneShot(mediumWarningClip);
                
            Debug.Log("[AudioTelemetry] 🔵 Medium Warning triggered (e.g. Frame skip / Drift).");
        }

        public void PlayLogicError()
        {
            if (logicErrorClip != null && audioSource != null) 
                audioSource.PlayOneShot(logicErrorClip);
                
            Debug.LogError("[AudioTelemetry] 🟠 Logic/Connection Error triggered.");
        }

        public void PlayFatalError()
        {
            if (fatalErrorClip != null && audioSource != null) 
                audioSource.PlayOneShot(fatalErrorClip);
                
            Debug.LogError("[AudioTelemetry] 🔴 FATAL ERROR / Tracking loss triggered.");
        }
    }
}
