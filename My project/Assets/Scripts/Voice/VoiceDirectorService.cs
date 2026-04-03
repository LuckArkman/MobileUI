using UnityEngine;
using System.Collections.Generic;

namespace LuckArkman.XR.Voice
{
    /// <summary>
    /// Serviço centralizador de fila de voz com prioridade.
    /// Garante que alertas de obstáculos (MiDaS/YOLO) sempre
    /// interrompam instruções de navegação secundárias.
    ///
    /// Hierarquia de prioridade:
    ///   0 - Obstacle : MiDaS/YOLO → interrompe TUDO imediatamente
    ///   1 - Navigation: Google Maps e GPS → aguarda slot livre
    ///   2 - System   : Conexão, bateria → menor prioridade
    /// </summary>
    public class VoiceDirectorService : MonoBehaviour
    {
        [Header("Motor de Voz")]
        [Tooltip("Referência ao SmallPrinceTTS no mesmo GameObject ou na cena.")]
        public SmallPrinceTTS ttsEngine;

        // Filas separadas por prioridade para evitar mistura de mensagens
        private readonly Queue<string> _obstacleQueue   = new Queue<string>();
        private readonly Queue<string> _navigationQueue = new Queue<string>();
        private readonly Queue<string> _systemQueue     = new Queue<string>();

        // ─── API Pública ──────────────────────────────────────────────────

        /// <summary>
        /// Enfileira ou emite imediatamente uma mensagem de voz.
        /// </summary>
        /// <param name="text">Texto a ser falado.</param>
        /// <param name="priority">Nível de prioridade da mensagem.</param>
        public void Enqueue(string text, VoicePriority priority)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (ttsEngine == null)
            {
                Debug.LogWarning($"[VoiceDirector] ttsEngine não configurado. Mensagem: «{text}»");
                return;
            }

            if (priority == VoicePriority.Obstacle)
            {
                // Obstáculos interrompem IMEDIATAMENTE o que estiver tocando.
                // Limpa também a fila de obstáculos para evitar repetição de alertas
                // que já ficaram desatualizados durante o tempo de processamento.
                _obstacleQueue.Clear();
                ttsEngine.InterruptAndSpeak(text);
                return;
            }

            switch (priority)
            {
                case VoicePriority.Navigation:
                    _navigationQueue.Enqueue(text);
                    break;
                case VoicePriority.System:
                    _systemQueue.Enqueue(text);
                    break;
            }
        }

        // ─── Processamento de Fila ────────────────────────────────────────

        private void Update()
        {
            // Não processa nada enquanto o TTS estiver falando
            if (ttsEngine == null || ttsEngine.IsSpeaking) return;

            // Processa na ordem de prioridade: Obstacle > Navigation > System
            if (_obstacleQueue.Count > 0)
            {
                ttsEngine.Speak(_obstacleQueue.Dequeue());
            }
            else if (_navigationQueue.Count > 0)
            {
                ttsEngine.Speak(_navigationQueue.Dequeue());
            }
            else if (_systemQueue.Count > 0)
            {
                ttsEngine.Speak(_systemQueue.Dequeue());
            }
        }
    }
}
