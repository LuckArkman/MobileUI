using System.Collections.Generic;
using UnityEngine;

namespace LuckArkman.XR.Navigation
{
    public class RouteProgressTracker : MonoBehaviour
    {
        [Header("Rota e Usuário")]
        [Tooltip("Transform do usuário ou do dispositivo móvel usado como referência de posição.")]
        public Transform userTransform;

        [Tooltip("Lista de waypoints reais que formam a rota de navegação.")]
        public List<Transform> routeWaypoints = new List<Transform>();

        [Tooltip("Raio de tolerância em metros para considerar um waypoint como alcançado.")]
        public float radiusTolerance = 1.5f;

        [Tooltip("Habilita logs de auditoria de progresso de rota.")]
        public bool debugLogs = true;

        [HideInInspector]
        public int currentWaypointIndex = 0;

        public bool IsRouteComplete => currentWaypointIndex >= routeWaypoints.Count;

        public Transform CurrentWaypoint => !IsRouteComplete ? routeWaypoints[currentWaypointIndex] : null;

        private void Start()
        {
            if (userTransform == null)
            {
                userTransform = transform;
            }

            if (debugLogs)
            {
                Debug.Log($"[RouteProgress] Iniciado com {routeWaypoints.Count} waypoints. Tolerância = {radiusTolerance:F2} m.");
            }
        }

        private void Update()
        {
            if (IsRouteComplete || CurrentWaypoint == null) return;

            float distance = Vector3.Distance(userTransform.position, CurrentWaypoint.position);
            if (distance <= radiusTolerance)
            {
                if (debugLogs)
                {
                    Debug.Log($"[RouteProgress] Waypoint {currentWaypointIndex + 1}/{routeWaypoints.Count} atingido. Distância = {distance:F2} m.");
                }

                currentWaypointIndex++;

                if (IsRouteComplete)
                {
                    if (debugLogs)
                    {
                        Debug.Log("[RouteProgress] Rota concluída. Todos os waypoints foram alcançados.");
                    }
                }
                else if (debugLogs)
                {
                    Debug.Log($"[RouteProgress] Próximo waypoint ativado: {currentWaypointIndex + 1}. Distância atual = {Vector3.Distance(userTransform.position, CurrentWaypoint.position):F2} m.");
                }
            }
        }

        public float GetDistanceToCurrentWaypoint()
        {
            return CurrentWaypoint == null ? 0f : Vector3.Distance(userTransform.position, CurrentWaypoint.position);
        }
    }
}
