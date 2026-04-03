namespace LuckArkman.XR.Voice
{
    /// <summary>
    /// Níveis de prioridade do sistema de voz.
    /// Valores menores = maior prioridade.
    /// </summary>
    public enum VoicePriority
    {
        /// <summary>Alertas de obstáculos do MiDaS/YOLO. Interrompe tudo.</summary>
        Obstacle = 0,

        /// <summary>Instruções turn-by-turn do Google Maps / GPS local.</summary>
        Navigation = 1,

        /// <summary>Mensagens de sistema: conexão, bateria, erros.</summary>
        System = 2
    }
}
