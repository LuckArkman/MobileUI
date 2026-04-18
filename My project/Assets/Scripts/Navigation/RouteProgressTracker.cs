using System;
using System.Collections.Generic;
using UnityEngine;
using LuckArkman.XR.Networking; // NavigationManager (GPS fallback)

namespace LuckArkman.XR.Navigation
{
    /// <summary>
    /// SISTEMA UNIFICADO DE CHECKPOINTS — App La Rosa
    ///
    /// Esta classe é a AUTORIDADE PRINCIPAL de progresso de rota.
    /// É aqui que o sistema sabe em qual checkpoint o usuário está,
    /// qual é o próximo objetivo, e quando ele chegou lá.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// SEPARAÇÃO DE RESPONSABILIDADES (não confundir com RouteProgressManager):
    ///
    ///   RouteProgressTracker  ← VOCÊ ESTÁ AQUI
    ///     Responsabilidade: "Onde o usuário está na rota?"
    ///     Detecta chegada a checkpoints 3D (ARCore/IMU) ou GPS (fallback).
    ///     Expõe CurrentWaypoint, AnguloRelativoAoWaypoint e DistanciaAoWaypointAtual.
    ///     Dispara eventos (OnCheckpointReached, OnRouteComplete) para o Guia.
    ///
    ///   RouteProgressManager  (em GuiaVOZ/)
    ///     Responsabilidade: "O que fazer quando há um obstáculo?"
    ///     Gerencia sequências de evasão de obstáculos (MiDaS + YOLO).
    ///     Persiste o índice de checkpoint de ÁUDIO (roteiro de apresentação).
    ///     Não sabe de posição — consome eventos deste script.
    /// ─────────────────────────────────────────────────────────────────────────
    ///
    /// MODOS DE OPERAÇÃO:
    ///   useGPSFallback = false  →  Modo AR (padrão): usa waypoints 3D da cena Unity.
    ///                              Funciona em ambientes internos mapeados com ARCore/ARKit.
    ///   useGPSFallback = true   →  Modo GPS: delega ao NavigationManager (lat/lon).
    ///                              Útil para rotas ao ar livre longas sem mapeamento 3D.
    /// </summary>
    public class RouteProgressTracker : MonoBehaviour
    {
        // =====================================================================
        // SEÇÃO 1 — REFERÊNCIAS E ROTA
        // =====================================================================

        [Header("Rota e Usuário")]
        [Tooltip("Transform do usuário ou do dispositivo móvel. Se nulo, usa o próprio GameObject.")]
        public Transform userTransform;

        [Tooltip("Lista ordenada de waypoints 3D que formam a rota. Usados quando useGPSFallback = false.")]
        public List<Transform> routeWaypoints = new List<Transform>();

        [Tooltip("Raio de tolerância (metros) para considerar um waypoint como alcançado.")]
        [Range(0.3f, 5.0f)]
        public float radiusTolerance = 1.5f;

        // =====================================================================
        // SEÇÃO 2 — MODO GPS FALLBACK
        // =====================================================================

        [Header("Modo de Operação")]
        [Tooltip(
            "false = Modo AR (padrão): usa waypoints Transform 3D da cena.\n" +
            "true  = Modo GPS: delega ao NavigationManager para rotas ao ar livre.\n\n" +
            "Use false em ambientes internos mapeados com ARCore/ARKit.\n" +
            "Use true  em percursos externos longos sem mapeamento 3D."
        )]
        public bool useGPSFallback = false;

        // =====================================================================
        // SEÇÃO 3 — INTEGRAÇÃO COM ODOMETRY
        // =====================================================================

        [Header("Integração com Pedômetro")]
        [Tooltip(
            "Referência opcional ao OdometryTracker.\n" +
            "Quando conectado, o tracker pode usar contagem de passos para confirmar\n" +
            "a chegada ao waypoint, além da distância física.\n" +
            "Útil quando o ARCore tem drift e a posição 3D é imprecisa."
        )]
        public OdometryTracker odometryTracker;

        [Tooltip(
            "Número mínimo de passos percorridos desde o início do checkpoint\n" +
            "para que a chegada seja confirmada por contagem de passos.\n" +
            "Só aplicado se useOdometryConfirmation = true."
        )]
        [Range(1, 20)]
        public int passosParaConfirmarCheckpoint = 5;

        [Tooltip(
            "Se verdadeiro, exige contagem de passos ALÉM de proximidade física\n" +
            "para confirmar a chegada ao waypoint. Aumenta robustez contra drift."
        )]
        public bool useOdometryConfirmation = false;

        // =====================================================================
        // SEÇÃO 4 — SAÍDA PARA A BÚSSOLA ROSA (SERVO)
        // =====================================================================

        [Header("Bússola Rosa — Saída de Ângulo")]
        [Tooltip(
            "Quando verdadeiro, calcula o ângulo de servo (0–180°) em direção\n" +
            "ao próximo waypoint para enviar à Bússola Rosa via ActuatorClient.\n" +
            "  0°  = virar completamente à esquerda\n" +
            " 90°  = destino à frente (servo centralizado)\n" +
            "180°  = virar completamente à direita"
        )]
        public bool calcularAnguloBussola = true;

        // =====================================================================
        // SEÇÃO 5 — DEBUG
        // =====================================================================

        [Header("Debug")]
        public bool debugLogs = true;

        // =====================================================================
        // SEÇÃO 6 — ESTADO INTERNO
        // =====================================================================

        [HideInInspector]
        public int currentWaypointIndex = 0;

        private int _passosAoIniciarCheckpoint = 0;

        // =====================================================================
        // SEÇÃO 7 — PROPRIEDADES PÚBLICAS
        // =====================================================================

        /// <summary>True quando todos os waypoints foram alcançados (ou GPS reportou conclusão).</summary>
        public bool IsRouteComplete
        {
            get
            {
                if (useGPSFallback && NavigationManager.Instance != null)
                    return !NavigationManager.Instance.isNavigating;
                return currentWaypointIndex >= routeWaypoints.Count;
            }
        }

        /// <summary>Transform do waypoint atual. Null quando rota completa ou em modo GPS.</summary>
        public Transform CurrentWaypoint
        {
            get
            {
                if (useGPSFallback || IsRouteComplete) return null;
                return routeWaypoints[currentWaypointIndex];
            }
        }

        /// <summary>Distância em metros até o waypoint atual.</summary>
        public float DistanciaAoWaypointAtual
        {
            get
            {
                if (useGPSFallback && NavigationManager.Instance != null)
                    return NavigationManager.Instance.distanciaAoDestino;
                return CurrentWaypoint == null ? 0f :
                       Vector3.Distance(userTransform.position, CurrentWaypoint.position);
            }
        }

        /// <summary>
        /// Ângulo relativo em graus ao próximo waypoint.
        /// Positivo = waypoint à direita | Negativo = à esquerda | 0 = alinhado.
        /// Consumido por Decision.cs para calcular intenção de navegação.
        /// </summary>
        public float AnguloRelativoAoWaypoint { get; private set; }

        /// <summary>
        /// Ângulo absoluto (0–180°) para o servo da Bússola Rosa.
        /// Calculado só quando calcularAnguloBussola = true.
        /// </summary>
        public int AnguloParaBussola { get; private set; } = 90;

        // =====================================================================
        // SEÇÃO 8 — EVENTOS
        // =====================================================================

        /// <summary>
        /// Disparado quando o usuário alcança um waypoint.
        /// Parâmetro: índice do waypoint atingido (base 0).
        /// O RouteProgressManager escuta este evento para avançar o áudio do checkpoint.
        /// </summary>
        public event Action<int> OnCheckpointReached;

        /// <summary>Disparado quando todos os waypoints foram percorridos.</summary>
        public event Action OnRouteComplete;

        // =====================================================================
        // SEÇÃO 9 — CICLO DE VIDA UNITY
        // =====================================================================

        private void Start()
        {
            if (userTransform == null)
                userTransform = transform;

            if (useGPSFallback)
            {
                Debug.Log("[RouteProgressTracker] 🛰️ Modo GPS ativo — delegando ao NavigationManager.");
            }
            else
            {
                if (routeWaypoints.Count == 0)
                    Debug.LogWarning("[RouteProgressTracker] ⚠️ Nenhum waypoint configurado. " +
                                     "Adicione Transforms à lista routeWaypoints no Inspector.");
                else if (debugLogs)
                    Debug.Log($"[RouteProgressTracker] ✅ Iniciado com {routeWaypoints.Count} waypoints. " +
                              $"Tolerância = {radiusTolerance:F2}m. " +
                              $"Confirmação por passos: {useOdometryConfirmation}.");
            }

            _passosAoIniciarCheckpoint = odometryTracker != null ? odometryTracker.StepCount : 0;
        }

        private void Update()
        {
            if (IsRouteComplete) return;

            AtualizarAngulo();

            if (!useGPSFallback && CurrentWaypoint != null)
                VerificarChegada();
        }

        // =====================================================================
        // SEÇÃO 10 — LÓGICA INTERNA
        // =====================================================================

        private void AtualizarAngulo()
        {
            if (useGPSFallback && NavigationManager.Instance != null)
            {
                AnguloRelativoAoWaypoint = NavigationManager.Instance.anguloRelativoAoDestino;
            }
            else if (CurrentWaypoint != null && userTransform != null)
            {
                Vector3 dir = CurrentWaypoint.position - userTransform.position;
                dir.y = 0f;
                AnguloRelativoAoWaypoint = Vector3.SignedAngle(
                    userTransform.forward,
                    dir.normalized,
                    Vector3.up
                );
            }
            else
            {
                AnguloRelativoAoWaypoint = 0f;
            }

            // Converte ângulo relativo [-90, +90] → servo [0, 180]
            // Clampado em ±90° pois o servo tem alcance de 180° total.
            if (calcularAnguloBussola)
            {
                float servoFloat  = 90f + Mathf.Clamp(AnguloRelativoAoWaypoint, -90f, 90f);
                AnguloParaBussola = Mathf.RoundToInt(Mathf.Clamp(servoFloat, 0f, 180f));
            }
        }

        private void VerificarChegada()
        {
            float distancia          = Vector3.Distance(userTransform.position, CurrentWaypoint.position);
            bool  chegouPorDistancia = distancia <= radiusTolerance;

            bool chegouPorPassos = true;
            if (useOdometryConfirmation && odometryTracker != null)
            {
                int passosPercorridos = odometryTracker.StepCount - _passosAoIniciarCheckpoint;
                chegouPorPassos = passosPercorridos >= passosParaConfirmarCheckpoint;
            }

            if (!chegouPorDistancia || !chegouPorPassos) return;

            int indiceAtingido = currentWaypointIndex;

            if (debugLogs)
                Debug.Log($"[RouteProgressTracker] ✅ Waypoint {indiceAtingido + 1}/{routeWaypoints.Count} atingido. " +
                          $"Dist={distancia:F2}m | Servo={AnguloParaBussola}°.");

            currentWaypointIndex++;
            _passosAoIniciarCheckpoint = odometryTracker != null ? odometryTracker.StepCount : 0;

            OnCheckpointReached?.Invoke(indiceAtingido);

            if (IsRouteComplete)
            {
                Debug.Log("[RouteProgressTracker] 🏁 Rota concluída! Todos os waypoints alcançados.");
                OnRouteComplete?.Invoke();
            }
            else if (debugLogs)
            {
                Debug.Log($"[RouteProgressTracker] → Próximo: waypoint {currentWaypointIndex + 1}. " +
                          $"Dist={DistanciaAoWaypointAtual:F1}m.");
            }
        }

        // =====================================================================
        // SEÇÃO 11 — API PÚBLICA
        // =====================================================================

        /// <summary>Retorna distância ao waypoint atual. Mantém compatibilidade com Decision.cs.</summary>
        public float GetDistanceToCurrentWaypoint() => DistanciaAoWaypointAtual;

        /// <summary>Reinicia a rota para o primeiro waypoint.</summary>
        public void ResetarRota()
        {
            currentWaypointIndex       = 0;
            _passosAoIniciarCheckpoint = odometryTracker != null ? odometryTracker.StepCount : 0;
            odometryTracker?.ResetarContagem();
            Debug.Log("[RouteProgressTracker] 🔄 Rota reiniciada para o waypoint 0.");
        }

        /// <summary>Avança manualmente para o próximo waypoint (ex: botão de confirmação no HUD).</summary>
        public void AvancarManualmente()
        {
            if (IsRouteComplete) return;
            int indiceAnterior = currentWaypointIndex;
            currentWaypointIndex++;
            _passosAoIniciarCheckpoint = odometryTracker != null ? odometryTracker.StepCount : 0;
            OnCheckpointReached?.Invoke(indiceAnterior);
            if (IsRouteComplete) OnRouteComplete?.Invoke();
            if (debugLogs)
                Debug.Log($"[RouteProgressTracker] ⏭️ Avançado manualmente para waypoint {currentWaypointIndex}.");
        }

        // =====================================================================
        // SEÇÃO 12 — GIZMOS (Scene View)
        // =====================================================================

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (routeWaypoints == null || routeWaypoints.Count == 0) return;

            for (int i = 0; i < routeWaypoints.Count; i++)
            {
                if (routeWaypoints[i] == null) continue;

                bool passado = i < currentWaypointIndex;
                bool atual   = i == currentWaypointIndex;

                Gizmos.color = passado ? Color.green : atual ? Color.yellow : Color.cyan;
                Gizmos.DrawSphere(routeWaypoints[i].position, radiusTolerance);

                if (i < routeWaypoints.Count - 1 && routeWaypoints[i + 1] != null)
                {
                    Gizmos.color = passado ? new Color(0f, 1f, 0f, 0.4f) : new Color(0f, 1f, 1f, 0.4f);
                    Gizmos.DrawLine(routeWaypoints[i].position, routeWaypoints[i + 1].position);
                }

                UnityEditor.Handles.Label(
                    routeWaypoints[i].position + Vector3.up * 0.3f,
                    $"CP{i + 1}"
                );
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft,
                richText  = true
            };

            string modo   = useGPSFallback ? "🛰️ GPS" : "📐 AR 3D";
            string cor    = IsRouteComplete ? "lime" : "cyan";
            string estado = IsRouteComplete ? "CONCLUÍDA" :
                            $"CP {currentWaypointIndex + 1}/{(routeWaypoints.Count > 0 ? routeWaypoints.Count.ToString() : "?")}";

            string texto =
                $"<color={cor}>🗺️ [RouteProgressTracker] {estado} [{modo}]</color>\n" +
                $"Dist: {DistanciaAoWaypointAtual:F1}m | " +
                $"Ângulo: {AnguloRelativoAoWaypoint:F0}° | " +
                $"Servo 🌹: {AnguloParaBussola}°";

            GUI.Box(new Rect(10, 285, 480, 60), texto, style);
        }
#endif
    }
}
