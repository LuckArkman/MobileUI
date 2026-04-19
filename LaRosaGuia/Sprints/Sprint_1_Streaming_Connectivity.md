# Sprint 1: Streaming & Conectividade de Baixa Latência

## Visão Geral
Esta sprint foca na implementação do "encanamento" principal do sistema: a transmissão de vídeo do headset VR (Oculus) para o aplicativo Android Android via Wi-Fi, priorizando a latência mínima (< 50ms).

## Objetivos
- Estabelecer conexão estável entre Oculus e Android.
- Implementar pipeline de captura e codificação no headset.
- Implementar decodificação e exibição em tempo real no Unity/Android.

## Ferramentas e Pacotes
- **Unity WebRTC Package**: Para streaming de baixa latência e sinalização.
- **Unity Render Streaming**: Para facilitar o pipeline de codificação/streaming.
- **Native WebSocket**: Para troca de metadados e controle.
- **Protocolos**: WebRTC (vídeo), UDP (comandos rápidos).

## Scripts a Implementar
1. `StreamServerManager.cs`: (Lado VR) Gerencia a captura da câmera do headset e o envio dos frames via WebRTC.
2. `StreamClientReceiver.cs`: (Lado AR) Recebe os fluxos de vídeo e os renderiza em uma textura (RawImage ou Material).
3. `NetworkDiscovery.cs`: Utiliza UDP broadcast para que o Android encontre o headset na rede local automaticamente.
4. `LatencyMonitor.cs`: Mede o RTT (Round Trip Time) e a latência de vídeo para ajuste dinâmico de bitrate.

## Tarefas
- [ ] Configurar pacotes WebRTC e Render Streaming no projeto.
- [ ] Criar cena de teste no headset para capturar o "Eye View".
- [ ] Implementar sistema de handshake via WebSocket.
- [ ] Realizar testes de latência em rede 5GHz.
- [ ] Implementar UI de status de rede (Wi-Fi Signal, Latency, Bitrate).
