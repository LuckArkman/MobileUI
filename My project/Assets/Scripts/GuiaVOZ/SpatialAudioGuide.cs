using UnityEngine;

namespace LuckArkman.XR.Main
{
    public class SpatialAudioGuide : MonoBehaviour
    {
        [Header("Configuração de Áudio 3D")]
        [Tooltip("O AudioSource que vai tocar a voz da IA")]
        public AudioSource voiceAudioSource;

        [Tooltip("Força do direcionamento do som (0 = Centro, 1 = Totalmente de um lado)")]
        [Range(0f, 1f)] public float intensidadeDoPan = 0.8f; 

        private void Start()
        {
            if (voiceAudioSource != null)
            {
                // Garante que o Unity vai respeitar o Pan (2D/3D balance)
                voiceAudioSource.spatialBlend = 0f; 
                
                // Acopla visualmente e aciona no Áudio o Controlador/Equalizador de Voz Automático
                if (voiceAudioSource.gameObject.GetComponent<VoiceAudioController>() == null)
                {
                    voiceAudioSource.gameObject.AddComponent<VoiceAudioController>();
                    Debug.Log("[Spatial Audio] Processador de Equalização (Graves/Médios/Agudos) anexado automaticamente!");
                }
            }
        }

        // Função chamada pelo Guia.cs antes de dar o Play no áudio
        public void AjustarDirecaoDoSom(Guia.EstadoInstrucao comando)
        {
            if (voiceAudioSource == null) return;

            switch (comando)
            {
                // ==========================================
                // SOM NA ORELHA DIREITA
                // ==========================================
                case Guia.EstadoInstrucao.DesviarDireita:
                case Guia.EstadoInstrucao.GirarDireita:
                case Guia.EstadoInstrucao.DesviarDuploDireita: // Adicionado o passo duplo
                    voiceAudioSource.panStereo = intensidadeDoPan; 
                    break;

                // ==========================================
                // SOM NA ORELHA ESQUERDA
                // ==========================================
                case Guia.EstadoInstrucao.DesviarEsquerda:
                case Guia.EstadoInstrucao.GirarEsquerda:
                case Guia.EstadoInstrucao.DesviarDuploEsquerda: // Adicionado o passo duplo
                    voiceAudioSource.panStereo = -intensidadeDoPan; 
                    break;

                // ==========================================
                // SOM CENTRALIZADO (FRENTE E PARADAS)
                // ==========================================
                case Guia.EstadoInstrucao.Parar:
                case Guia.EstadoInstrucao.Frente1: // Novas progressões frontais centralizadas
                case Guia.EstadoInstrucao.Frente2:
                case Guia.EstadoInstrucao.Frente3:
                case Guia.EstadoInstrucao.Frente4:
                case Guia.EstadoInstrucao.Nenhum:
                    voiceAudioSource.panStereo = 0f; 
                    break;
            }
        }
    }
}