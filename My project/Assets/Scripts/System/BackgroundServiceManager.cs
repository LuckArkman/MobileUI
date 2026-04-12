using UnityEngine;

// Directiva de compilação: o bloco Android só compila quando o target é Android.
// Isso garante zero erros no Editor Windows e em builds iOS.
#if UNITY_ANDROID
using System;
#endif

namespace LuckArkman.XR.Background
{
    /// <summary>
    /// Gerencia a execução contínua do aplicativo em segundo plano no Android.
    ///
    /// Responsabilidades:
    ///   1. Inicia um Android Foreground Service (MidasForegroundService.java) para
    ///      que o sistema operacional não encerre o processo quando a tela bloquear.
    ///   2. Adquire um WakeLock PARTIAL para manter a CPU ativa com a tela apagada.
    ///   3. Libera todos os recursos de forma limpa apenas quando o usuário
    ///      fechar o aplicativo explicitamente.
    ///
    /// Uso na cena:
    ///   Adicione este componente ao mesmo GameObject que o MainSystemOrchestrator.
    ///   Não é necessária nenhuma referência manual no Inspector.
    /// </summary>
    public class BackgroundServiceManager : MonoBehaviour
    {
        // Nome completo da classe Java do Foreground Service.
        // Deve corresponder EXATAMENTE ao package + nome da classe em MidasForegroundService.java.
        private const string SERVICE_CLASS = "com.luckarkman.xr.MidasForegroundService";

        // Tag identificadora do WakeLock — usada nos logs do Android (adb logcat).
        private const string WAKELOCK_TAG = "LuckArkman:MidasWakeLock";

#if UNITY_ANDROID
        // Referência ao objeto WakeLock do Android.
        // Declarado fora do método para poder ser liberado no OnDestroy.
        private AndroidJavaObject _wakeLockObject = null;

        // Flag para controlar se o serviço foi iniciado com sucesso
        // (evita tentar parar um serviço que nunca foi iniciado).
        private bool _serviceStarted = false;
#endif

        // ================================================================
        // CICLO DE VIDA
        // ================================================================

        private void Awake()
        {
#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android)
            {
                // Requisita a permissão IMEDIATAMENTE no despertar do app
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
                {
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
                }
            }
#endif
        }

        private void Start()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.runInBackground = true;

#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android)
            {
                StartCoroutine(WaitAndInitializeBackground());
            }
#endif
        }

        private System.Collections.IEnumerator WaitAndInitializeBackground()
        {
#if UNITY_ANDROID
            // Aguarda o usuário responder ao diálogo que surgiu no Awake
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
            {
                yield return new WaitForSeconds(1.0f);
            }

            InitializeAndroidBackground();
#else
            yield break;
#endif
        }

#if UNITY_ANDROID
        /// <summary>
        /// Ponto de entrada para toda a inicialiazção Android.
        /// Separado do Start() para clareza e isolamento.
        /// </summary>
        private void InitializeAndroidBackground()
        {
            StartForegroundService();
            AcquireWakeLock();
        }

        // ================================================================
        // FOREGROUND SERVICE
        // ================================================================

        /// <summary>
        /// Inicia o MidasForegroundService como um Android Foreground Service.
        ///
        /// Por que Foreground Service?
        ///   - Processos normais em background são encerrados pelo Android após alguns minutos.
        ///   - Um Foreground Service exibe uma notificação persistente e recebe proteção
        ///     especial do sistema operacional contra encerramento automático.
        ///   - START_STICKY na classe Java garante a reinicialização automática
        ///     se o sistema forçar o encerramento por falta de memória.
        /// </summary>
        private void StartForegroundService()
        {
            try
            {
                // Obtém a Activity principal do Unity (contexto Android necessário para o Intent).
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // Cria um Intent explícito apontando para a nossa classe Java de serviço.
                    // Usamos setClassName(Context, String) ao invés do construtor Intent(Context, Class)
                    // porque é mais confiável no ambiente de interop Unity → Android.
                    using (var intent = new AndroidJavaObject("android.content.Intent"))
                    {
                        // setClassName retorna o próprio Intent (fluent API), mas não precisamos capturar.
                        intent.Call<AndroidJavaObject>("setClassName", activity, SERVICE_CLASS);

                        // Android 8.0 (API 26) em diante EXIGE startForegroundService() para
                        // serviços que chamam startForeground() internamente.
                        // Versões anteriores usam startService() normalmente.
                        int sdkVersion = GetAndroidSdkVersion();

                        if (sdkVersion >= 26)
                        {
                            activity.Call("startForegroundService", intent);
                        }
                        else
                        {
                            activity.Call("startService", intent);
                        }

                        _serviceStarted = true;
                        Debug.Log($"[BackgroundService] Foreground Service iniciado. " +
                                  $"(Android SDK {sdkVersion})");
                    }
                }
            }
            catch (Exception e)
            {
                // Não faz crash do app — o assistente funcionará, mas pode ser encerrado
                // pelo sistema em background. Logamos o erro para diagnóstico via ADB.
                Debug.LogError($"[BackgroundService] Falha ao iniciar Foreground Service: {e.Message}");
            }
        }

        // ================================================================
        // WAKELOCK
        // ================================================================

        /// <summary>
        /// Adquire um WakeLock PARTIAL do Android.
        ///
        /// PARTIAL_WAKE_LOCK (valor = 1):
        ///   - Mantém a CPU ativa.
        ///   - Permite que a TELA APAGUE (economia de bateria).
        ///   - Diferente de FULL_WAKE_LOCK que manteria a tela ligada.
        ///
        /// Por que precisamos?
        ///   - Mesmo com o Foreground Service ativo, o sistema pode suspender
        ///     a CPU em deep sleep. O WakeLock garante que o processamento
        ///     de áudio e GPS continue com a tela bloqueada.
        /// </summary>
        private void AcquireWakeLock()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // getSystemService("power") retorna o PowerManager do Android.
                    using (var powerManager = activity.Call<AndroidJavaObject>("getSystemService", "power"))
                    {
                        if (powerManager == null)
                        {
                            Debug.LogWarning("[BackgroundService] PowerManager não encontrado.");
                            return;
                        }

                        // PARTIAL_WAKE_LOCK = 0x00000001 = 1
                        // Este valor é uma constante estável do Android SDK desde API 1.
                        _wakeLockObject = powerManager.Call<AndroidJavaObject>(
                            "newWakeLock", 1, WAKELOCK_TAG
                        );

                        if (_wakeLockObject != null)
                        {
                            _wakeLockObject.Call("acquire");
                            Debug.Log("[BackgroundService] WakeLock PARTIAL adquirido. " +
                                      "CPU permanecerá ativa com tela bloqueada.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BackgroundService] Falha ao adquirir WakeLock: {e.Message}");
            }
        }

        // ================================================================
        // ENCERRAMENTO LIMPO
        // ================================================================

        /// <summary>
        /// Libera WakeLock e para o serviço.
        /// Chamado APENAS quando o usuário fecha o app explicitamente.
        /// NÃO é chamado quando a tela bloqueia ou o app vai para background.
        /// </summary>
        private void ReleaseAllResources()
        {
            // 1. Libera o WakeLock
            try
            {
                if (_wakeLockObject != null)
                {
                    bool isHeld = _wakeLockObject.Call<bool>("isHeld");
                    if (isHeld)
                    {
                        _wakeLockObject.Call("release");
                        Debug.Log("[BackgroundService] WakeLock liberado.");
                    }
                    _wakeLockObject.Dispose();
                    _wakeLockObject = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BackgroundService] Erro ao liberar WakeLock: {e.Message}");
            }

            // 2. Para o Foreground Service
            if (!_serviceStarted) return;

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    intent.Call<AndroidJavaObject>("setClassName", activity, SERVICE_CLASS);
                    activity.Call("stopService", intent);
                    Debug.Log("[BackgroundService] Foreground Service encerrado.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BackgroundService] Erro ao parar serviço: {e.Message}");
            }
        }

        // ================================================================
        // UTILITÁRIOS ANDROID
        // ================================================================

        /// <summary>
        /// Retorna o nível da API do Android instalada no dispositivo.
        /// Em caso de falha, retorna 21 (Android 5.0) como valor seguro mínimo.
        /// </summary>
        private int GetAndroidSdkVersion()
        {
            try
            {
                using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    return buildVersion.GetStatic<int>("SDK_INT");
                }
            }
            catch
            {
                return 21;
            }
        }
#endif

        // ================================================================
        // CALLBACKS DE CICLO DE VIDA DO UNITY
        // ================================================================

        private void OnApplicationPause(bool isPaused)
        {
            // Este callback é disparado quando o app vai para background (tela bloqueada,
            // app minimizado, etc.). NÃO encerramos nada aqui.
            // O Foreground Service e o WakeLock garantem a execução contínua.
            if (isPaused)
            {
                Debug.Log("[BackgroundService] App em background — serviço continua ativo.");
            }
            else
            {
                Debug.Log("[BackgroundService] App retornou ao foreground.");
            }
        }

        private void OnDestroy()
        {
            // OnDestroy é chamado quando o MonoBehaviour é destruído,
            // o que ocorre quando o usuário fecha o app.
#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android)
            {
                ReleaseAllResources();
            }
#endif
        }
    }
}
