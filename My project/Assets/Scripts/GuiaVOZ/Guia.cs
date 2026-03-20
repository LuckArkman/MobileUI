using UnityEngine;

namespace LuckArkman.XR.Main
{
    public class Guia : MonoBehaviour
    {
        // AQUI ESTÃO OS NOVOS ESTADOS!
        public enum EstadoInstrucao { Nenhum, Parar, DesviarDireita, DesviarEsquerda, GirarDireita, GirarEsquerda, DesviarDuploDireita, DesviarDuploEsquerda, Frente1, Frente2, Frente3, Frente4 }

        [Header("Módulos Integrados")]
        public SpatialAudioGuide spatialAudio; 

        [Header("Sistema de Voz Guia (Acessibilidade)")]
        public AudioSource voiceAudioSource;
        
        public AudioClip voiceMoveLeft;
        public AudioClip voiceMoveRight;
        public AudioClip voiceTurnLeft;
        public AudioClip voiceTurnRight;
        public AudioClip voiceStop;
        
        [Header("Vozes de Evasão Complexa")]
        public AudioClip voiceMoveDoubleLeft;  
        public AudioClip voiceMoveDoubleRight; 

        [Header("Vozes de Progressão Frontal")]
        public AudioClip voiceFrente1; // O Inspector vai mostrar isso aqui!
        public AudioClip voiceFrente2; 
        public AudioClip voiceFrente3; 
        public AudioClip voiceFrente4; 

        [Header("Tempos Dinâmicos de Espera (Segundos)")]
        public float tempoEsperaParar = 1.0f;     
        public float tempoEsperaAcao = 2.0f;      
        public float tempoEsperaContinuar = 3.5f; 
        
        private float proximoTempoDeFala = 0f;
        private EstadoInstrucao instrucaoAnterior = EstadoInstrucao.Nenhum;

        private void Start()
        {
            if (voiceAudioSource == null)
            {
                voiceAudioSource = gameObject.AddComponent<AudioSource>();
                voiceAudioSource.playOnAwake = false;
            }
        }

        public void ExecutarComando(EstadoInstrucao comandoDecidido)
        {
            if (Time.time >= proximoTempoDeFala)
            {
                if (comandoDecidido != instrucaoAnterior)
                {
                    TocarComandoDeVoz(comandoDecidido);
                    instrucaoAnterior = comandoDecidido;
                }
            }
        }

        private void TocarComandoDeVoz(EstadoInstrucao comando)
        {
            if (voiceAudioSource == null || voiceAudioSource.isPlaying) return;

            AudioClip clipParaTocar = null;
            float tempoDesteComando = tempoEsperaAcao;

            if (spatialAudio != null) spatialAudio.AjustarDirecaoDoSom(comando);

            switch (comando)
            {
                case EstadoInstrucao.Parar: clipParaTocar = voiceStop; tempoDesteComando = tempoEsperaParar; break;
                case EstadoInstrucao.GirarDireita: clipParaTocar = voiceTurnRight; break;
                case EstadoInstrucao.GirarEsquerda: clipParaTocar = voiceTurnLeft; break;
                case EstadoInstrucao.DesviarDireita: clipParaTocar = voiceMoveRight; break;
                case EstadoInstrucao.DesviarEsquerda: clipParaTocar = voiceMoveLeft; break;
                case EstadoInstrucao.DesviarDuploDireita: clipParaTocar = voiceMoveDoubleRight; break;
                case EstadoInstrucao.DesviarDuploEsquerda: clipParaTocar = voiceMoveDoubleLeft; break;
                
                // PROGRESSÃO FRONTAL CONECTADA AOS ÁUDIOS
                case EstadoInstrucao.Frente1: clipParaTocar = voiceFrente1; tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente2: clipParaTocar = voiceFrente2; tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente3: clipParaTocar = voiceFrente3; tempoDesteComando = tempoEsperaContinuar; break;
                case EstadoInstrucao.Frente4: clipParaTocar = voiceFrente4; tempoDesteComando = tempoEsperaContinuar; break;
            }

            if (clipParaTocar != null)
            {
                voiceAudioSource.clip = clipParaTocar;
                voiceAudioSource.Play();
                proximoTempoDeFala = Time.time + tempoDesteComando;
            }
        }
    }
}