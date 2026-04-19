using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.XR;

namespace LuckArkman.XR.Navigation
{
    /// <summary>
    /// Pedômetro e detector de movimento por IMU (Inertial Measurement Unit).
    ///
    /// Consome o acelerômetro nativo do celular para:
    ///   1. Detectar passos individuais via análise de picos de magnitude.
    ///   2. Estimar distância percorrida (passos × comprimento de passo).
    ///   3. Determinar se o usuário está em movimento ou parado (IsMoving).
    ///   4. Disparar eventos de passo/início/fim de movimento para outros sistemas.
    ///
    /// Integração principal:
    ///   → MainSystemOrchestrator usa IsMoving para destravar comandos evasivos
    ///     apenas quando o usuário efetivamente caminhou (substituindo o timer fixo de 1.5s).
    ///   → RouteProgressManager usa StepCount para confirmar que o usuário obedeceu
    ///     ao comando antes de re-emitir a mesma instrução.
    ///
    /// Limitações conhecidas:
    ///   → O acelerômetro inclui gravidade estática (~1g). O limiar de pico é relativo
    ///     a essa base, não é necessário remover a gravidade explicitamente.
    ///   → Dispositivos com muito jitter de hardware podem precisar ajuste de stepPeakThreshold.
    ///   → Não rasteia posição 3D absoluta (sem fusão com GPS/ARCore neste script).
    /// </summary>
    public class OdometryTracker : MonoBehaviour
    {
        // =====================================================================
        // SEÇÃO 1 — CONFIGURAÇÃO DO PEDÔMETRO
        // =====================================================================

        [Header("Pedômetro — Detecção de Passos")]

        [Tooltip(
            "Variação mínima da magnitude do acelerômetro acima de 1g para\n" +
            "detectar um pico de passo. Aumente se gerar falsos positivos\n" +
            "(ex: vibração do motor / barulho de calçada irregular).\n" +
            "Recomendado: 0.30 para caminhada normal, 0.20 para idosos."
        )]
        [Range(0.15f, 1.5f)]
        public float stepPeakThreshold = 0.30f;

        [Tooltip(
            "Intervalo mínimo (segundos) entre dois passos consecutivos.\n" +
            "Evita dupla-contagem quando o pico de aceleração é largo.\n" +
            "Recomendado: 0.25–0.40s (caminhada normal = 1.2–2.0 passos/s)."
        )]
        [Range(0.20f, 0.80f)]
        public float minStepInterval = 0.30f;

        [Tooltip(
            "Comprimento médio de um passo em metros.\n" +
            "Usado para converter contagem de passos em distância estimada.\n" +
            "Valor padrão (0.75m) alinhado com o RaycastScanner."
        )]
        [Range(0.40f, 1.20f)]
        public float comprimentoPasso = 0.75f;

        // =====================================================================
        // SEÇÃO 2 — CONFIGURAÇÃO DE DETECÇÃO DE MOVIMENTO
        // =====================================================================

        [Header("Detector de Movimento")]

        [Tooltip(
            "Segundos sem passo detectado para declarar o usuário PARADO.\n" +
            "Ex: 2.0s → se nenhum passo ocorrer em 2s, IsMoving = false.\n" +
            "Valores menores tornam o sistema mais responsivo mas menos tolerante a pausas curtas."
        )]
        [Range(0.5f, 6.0f)]
        public float idleTimeoutSegundos = 2.0f;

        [Tooltip(
            "Variância mínima do acelerômetro (janela de 20 amostras)\n" +
            "para considerar movimento mesmo sem pico de passo.\n" +
            "Detecta movimentos lentos ou ajustes de posição sem passada completa.\n" +
            "Recomendado: 0.005–0.015."
        )]
        [Range(0.001f, 0.05f)]
        public float varianciaMovimento = 0.008f;

        [Tooltip(
            "Número mínimo de passos para liberar um comando evasivo bloqueado\n" +
            "pelo OrchestratorLock. O Orchestrator usa este valor para saber\n" +
            "que o usuário efetivamente obedeceu antes de re-emitir a instrução."
        )]
        [Range(1, 5)]
        public int passosParaDesbloquear = 1;

        // =====================================================================
        // SEÇÃO 2B — FUSÃO IMU + ARCORE/ARKIT
        // =====================================================================

        [Header("Fusão IMU + ARCore/ARKit")]
        [Tooltip(
            "Quando verdadeiro, usa a pose 6DoF do ARCore/ARKit para corrigir\n" +
            "o drift acumulado do acelerômetro (Feature Points da câmera).\n" +
            "Requer AR Foundation na cena. Fallback automático para IMU puro."
        )]
        public bool useARCoreFusion = true;

        [Tooltip(
            "Velocidade mínima (m/s) detectada pelo AR 6DoF para considerar movimento.\n" +
            "Complementa critérios de passos e variância (critério C)."
        )]
        [Range(0.01f, 0.5f)]
        public float arMovementThreshold = 0.05f;

        // =====================================================================
        // SEÇÃO 3 — SAÍDAS EM TEMPO REAL (Inspector)
        // =====================================================================

        [Header("Leitura em Tempo Real (somente leitura)")]
        [SerializeField] private bool  _isMoving;
        [SerializeField] private int   _stepCount;
        [SerializeField] private float _distanciaPercorridaMetros;
        [SerializeField] private float _velocidadeAtualMs;
        [SerializeField] private float _tempoSemPasso;
        [SerializeField] private float _varianciaAtual;

        // =====================================================================
        // SEÇÃO 4 — EVENTOS PÚBLICOS
        // =====================================================================

        /// <summary>
        /// Disparado uma vez a cada passo detectado.
        /// O Orchestrator escuta este evento para decrementar o contador de bloqueio
        /// e liberar o próximo ciclo de comandos evasivos.
        /// </summary>
        public event Action OnPassoDetectado;

        /// <summary>Disparado na primeira detecção de movimento após um período parado.</summary>
        public event Action OnUsuarioComecouMover;

        /// <summary>Disparado quando o usuário para após estar em movimento.</summary>
        public event Action OnUsuarioParou;

        // =====================================================================
        // SEÇÃO 5 — PROPRIEDADES PÚBLICAS (leitura)
        // =====================================================================

        /// <summary>
        /// True se o usuário estiver em movimento (passos recentes OU variância alta).
        /// O Orchestrator usa este valor para decidir se re-emite alertas.
        /// </summary>
        public bool IsMoving => _isMoving;

        /// <summary>Total de passos detectados desde o último Reset.</summary>
        public int StepCount => _stepCount;

        /// <summary>Distância total estimada em metros desde o último Reset.</summary>
        public float DistanciaMetros => _distanciaPercorridaMetros;

        /// <summary>Velocidade estimada de caminhada em metros por segundo.</summary>
        public float VelocidadeMs => _velocidadeAtualMs;

        /// <summary>Segundos decorridos desde o último passo detectado.</summary>
        public float TempoSemPasso => _tempoSemPasso;

        /// <summary>
        /// Número de passos dados desde o último comando evasivo.
        /// Resetado pelo Orchestrator ao emitir um comando. Lido para decidir re-emissão.
        /// </summary>
        public int PassosDesdeUltimoComando => _passosDesdeUltimoComando;

        // =====================================================================
        // SEÇÃO 6 — ESTADO INTERNO
        // =====================================================================

        // Pedômetro
        private float _lastStepTime       = -999f;
        private bool  _wasAboveThreshold  = false;
        private int   _passosDesdeUltimoComando = 0;

        // Detector de movimento
        private bool  _wasMoving = false;

        // Fusão com ARCore/ARKit
        private InputDevice _arHeadDevice;
        private bool        _arDisponivel    = false;
        private Vector3     _arPosicaoAnterior = Vector3.zero;
        private float       _arVelocidade    = 0f;
        private bool        _arInicializado  = false;

        // Buffer circular para variância (detecção de micro-movimentos)
        private const int TAMANHO_BUFFER = 20;
        private readonly Queue<float> _bufferMagnitude = new Queue<float>(TAMANHO_BUFFER);
        private float _somaMagnitude   = 0f;
        private float _somaMagnitudeSq = 0f;

        // =====================================================================
        // SEÇÃO 7 — CICLO DE VIDA UNITY
        // =====================================================================

        private void Awake()
        {
            // Ativa o giroscópio de hardware
            if (SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = true;
                Debug.Log("[OdometryTracker] ✅ Giroscópio ativado.");
            }
            else
            {
                Debug.LogWarning("[OdometryTracker] ⚠️ Giroscópio indisponível. Usando apenas acelerômetro.");
            }

            if (!SystemInfo.supportsAccelerometer)
            {
                Debug.LogError("[OdometryTracker] ❌ Acelerômetro indisponível. OdometryTracker não funcionará.");
            }

            // Tenta localizar o dispositivo AR (ARCore/ARKit)
            // O InputDevice com FloorHeight ou Position é o sinal de que AR está ativo
            if (useARCoreFusion)
            {
                var devices = new List<InputDevice>();
                InputDevices.GetDevices(devices);
                foreach (var d in devices)
                {
                    if (d.characteristics.HasFlag(InputDeviceCharacteristics.Camera) ||
                        d.characteristics.HasFlag(InputDeviceCharacteristics.HeadMounted))
                    {
                        _arHeadDevice  = d;
                        _arDisponivel  = true;
                        Debug.Log($"[OdometryTracker] 🔭 Dispositivo AR encontrado: {d.name}. Fusão IMU+AR ativa.");
                        break;
                    }
                }

                if (!_arDisponivel)
                    Debug.Log("[OdometryTracker] ℹ️ Nenhum dispositivo AR detectado. Usando apenas acelerômetro.");

                // Escuta conexão tardia de dispositivos AR (cena AR pode inicializar depois do Awake)
                InputDevices.deviceConnected += OnARDeviceConnected;
            }
        }

        private void OnDestroy()
        {
            InputDevices.deviceConnected -= OnARDeviceConnected;
        }

        private void OnARDeviceConnected(InputDevice device)
        {
            if (!_arDisponivel &&
                (device.characteristics.HasFlag(InputDeviceCharacteristics.Camera) ||
                 device.characteristics.HasFlag(InputDeviceCharacteristics.HeadMounted)))
            {
                _arHeadDevice = device;
                _arDisponivel = true;
                Debug.Log($"[OdometryTracker] 🔭 Dispositivo AR conectado tardiamente: {device.name}.");
            }
        }

        private void Update()
        {
            // ── Fusão IMU + ARCore/ARKit ─────────────────────────────────────
            // O AR fornece pose 6DoF precisa que corrige o drift do acelerômetro.
            // Quando disponível, _arVelocidade complementa ou substitui a variância.
            AtualizarPoseAR();

            // ── Leitura do Acelerômetro ──────────────────────────────────────
            // Input.acceleration retorna o vetor de aceleração do dispositivo,
            // incluindo a componente gravitacional (~1g orientada para baixo).
            // A magnitude total é ~1.0g em repouso e varia durante o caminhar.
            Vector3 aceleracaoBruta = Input.acceleration;
            float magnitude = aceleracaoBruta.magnitude;

            // ── Atualiza Buffer de Variância ─────────────────────────────────
            AtualizarBufferVariancia(magnitude);
            _varianciaAtual = CalcularVariancia();

            // ── Detecção de Passo ────────────────────────────────────────────
            DetectarPasso(magnitude);

            // ── Cálculo de Velocidade Estimada ───────────────────────────────
            _tempoSemPasso = Time.time - _lastStepTime;
            float frequenciaPasso = 1f / Mathf.Max(_tempoSemPasso, 0.001f);

            if (_tempoSemPasso < minStepInterval * 3f)
            {
                // Velocidade baseada na frequência de passos recentes
                _velocidadeAtualMs = Mathf.Clamp(comprimentoPasso * frequenciaPasso, 0f, 3.5f);
            }
            else
            {
                // Sem passos recentes: velocidade decai suavemente
                _velocidadeAtualMs = Mathf.MoveTowards(_velocidadeAtualMs, 0f, Time.deltaTime * 3f);
            }

            // ── Determinação de IsMoving ──────────────────────────────────────
            // Triplo critério:
            //   (A) Passos recentes: último passo há menos de idleTimeoutSegundos
            //   (B) Variância IMU: acelerômetro oscila acima do limiar (micro-movimentos)
            //   (C) Velocidade AR: ARCore/ARKit reporta deslocamento acima do limiar
            bool movendoPorPassos   = _tempoSemPasso < idleTimeoutSegundos;
            bool movendoPorVarianca = _varianciaAtual > varianciaMovimento;
            bool movendoPorAR       = useARCoreFusion && _arVelocidade > arMovementThreshold;
            _isMoving = movendoPorPassos || movendoPorVarianca || movendoPorAR;

            // ── Transições de Estado ─────────────────────────────────────────
            if (_isMoving && !_wasMoving)
            {
                OnUsuarioComecouMover?.Invoke();
                Debug.Log("[OdometryTracker] 🚶 Início de movimento detectado.");
            }
            else if (!_isMoving && _wasMoving)
            {
                OnUsuarioParou?.Invoke();
                Debug.Log($"[OdometryTracker] 🧍 Usuário parou. Passos totais: {_stepCount} | " +
                          $"Distância: {_distanciaPercorridaMetros:F1}m");
            }

            _wasMoving = _isMoving;
        }

        // =====================================================================
        // SEÇÃO 8 — FUSÃO AR: LEITURA DE POSE 6DoF
        // =====================================================================

        /// <summary>
        /// Lê a posição 6DoF do ARCore/ARKit via XR InputDevice.
        /// Calcula a velocidade de deslocamento real do celular no espaço físico.
        /// Quando ativo, este dado complementa o acelerômetro e o giroscópio para
        /// eliminar o drift de IMU que ocorre após longos períodos de caminhada.
        /// </summary>
        private void AtualizarPoseAR()
        {
            if (!useARCoreFusion || !_arDisponivel) return;

            if (!_arHeadDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 posicaoAR))
            {
                _arVelocidade = 0f;
                return;
            }

            if (!_arInicializado)
            {
                _arPosicaoAnterior = posicaoAR;
                _arInicializado    = true;
                return;
            }

            // Velocidade real = deslocamento / deltaTime (metros por segundo)
            float deslocamento = Vector3.Distance(posicaoAR, _arPosicaoAnterior);
            _arVelocidade       = deslocamento / Mathf.Max(Time.deltaTime, 0.001f);
            _arPosicaoAnterior  = posicaoAR;
        }

        // =====================================================================
        // SEÇÃO 9 — DETECÇÃO DE PASSO (ALGORITMO DE PICO)
        // =====================================================================

        /// <summary>
        /// Detecta um passo pela borda de subida do pico de aceleração.
        ///
        /// Algoritmo:
        ///   - Durante a caminhada, a magnitude oscila ~0.2–0.5g acima de 1g a cada passo.
        ///   - Detecta quando a magnitude CRUZA O LIMIAR de baixo para cima (borda de subida).
        ///   - Aplica anti-bounce temporal: ignora cruzamentos a menos de minStepInterval segundos.
        /// </summary>
        private void DetectarPasso(float magnitude)
        {
            // Limiar = 1g (static) + stepPeakThreshold (variação do passo)
            bool acimaDoLimiar = magnitude > (1f + stepPeakThreshold);

            // Detecta apenas a BORDA DE SUBIDA (transição false→true) para evitar dupla contagem
            if (acimaDoLimiar && !_wasAboveThreshold)
            {
                float intervalo = Time.time - _lastStepTime;
                if (intervalo >= minStepInterval)
                {
                    RegistrarPasso();
                }
            }

            _wasAboveThreshold = acimaDoLimiar;
        }

        private void RegistrarPasso()
        {
            _lastStepTime = Time.time;
            _stepCount++;
            _passosDesdeUltimoComando++;
            _distanciaPercorridaMetros = _stepCount * comprimentoPasso;

            OnPassoDetectado?.Invoke();

            Debug.Log($"[OdometryTracker] 👟 Passo #{_stepCount} | " +
                      $"Dist: {_distanciaPercorridaMetros:F1}m | " +
                      $"Desde cmd: {_passosDesdeUltimoComando}p");
        }

        // =====================================================================
        // SEÇÃO 9 — BUFFER DE VARIÂNCIA (DETECÇÃO DE MICRO-MOVIMENTO)
        // =====================================================================

        /// <summary>
        /// Mantém um buffer circular de magnitudes para cálculo eficiente de variância
        /// sem alocar memória a cada frame (algoritmo de variância online de Welford simplificado).
        /// </summary>
        private void AtualizarBufferVariancia(float valor)
        {
            if (_bufferMagnitude.Count >= TAMANHO_BUFFER)
            {
                float removido = _bufferMagnitude.Dequeue();
                _somaMagnitude   -= removido;
                _somaMagnitudeSq -= removido * removido;
            }

            _bufferMagnitude.Enqueue(valor);
            _somaMagnitude   += valor;
            _somaMagnitudeSq += valor * valor;
        }

        private float CalcularVariancia()
        {
            int n = _bufferMagnitude.Count;
            if (n < 2) return 0f;

            float media    = _somaMagnitude / n;
            float varianca = (_somaMagnitudeSq / n) - (media * media);
            return Mathf.Max(0f, varianca);
        }

        // =====================================================================
        // SEÇÃO 10 — API PÚBLICA
        // =====================================================================

        /// <summary>
        /// Chamado pelo MainSystemOrchestrator ao emitir um comando evasivo.
        /// Zera o contador de passos desde o último comando para que o sistema
        /// possa rastrear se o usuário efetivamente obedeceu antes de re-emitir.
        /// </summary>
        public void NotificarComandoEmitido()
        {
            _passosDesdeUltimoComando = 0;
            Debug.Log("[OdometryTracker] 🔄 Contador de passos pós-comando reiniciado.");
        }

        /// <summary>
        /// Indica se o usuário caminhou o suficiente desde o último comando
        /// para justificar a re-emissão de uma nova instrução evasiva.
        /// </summary>
        public bool UsuarioObedeceuComando()
        {
            return _passosDesdeUltimoComando >= passosParaDesbloquear;
        }

        /// <summary>
        /// Reseta toda a contagem de passos e distância (ex: nova rota iniciada).
        /// </summary>
        public void ResetarContagem()
        {
            _stepCount                  = 0;
            _passosDesdeUltimoComando   = 0;
            _distanciaPercorridaMetros  = 0f;
            _lastStepTime               = -999f;
            _velocidadeAtualMs          = 0f;
            _wasAboveThreshold          = false;
            Debug.Log("[OdometryTracker] 🔄 Contagem de passos e odometria resetadas.");
        }

        // =====================================================================
        // SEÇÃO 11 — DIAGNÓSTICO NO EDITOR
        // =====================================================================

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft,
                richText  = true
            };

            string corEstado = _isMoving ? "lime" : "orange";
            string icone     = _isMoving ? "🚶" : "🧍";
            string estado    = _isMoving ? "EM MOVIMENTO" : "PARADO";

            string corPassos = _passosDesdeUltimoComando >= passosParaDesbloquear ? "lime" : "yellow";

            string texto =
                $"<color=white>📍 [OdometryTracker]</color>\n" +
                $"<color={corEstado}>{icone} {estado}</color>  |  " +
                $"Passos: {_stepCount}  |  Dist: {_distanciaPercorridaMetros:F1}m  |  Vel: {_velocidadeAtualMs:F2} m/s\n" +
                $"Sem passo há: {_tempoSemPasso:F1}s  |  Variância: {_varianciaAtual:F4}  |  " +
                $"<color={corPassos}>Desde cmd: {_passosDesdeUltimoComando}/{passosParaDesbloquear}p</color>";

            GUI.Box(new Rect(10, 195, 480, 80), texto, style);
        }
#endif
    }
}
