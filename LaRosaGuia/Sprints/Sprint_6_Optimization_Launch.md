# Sprint 6: Otimização Mobile e Publicação

## Visão Geral
Refinamento final focado em performance (60 FPS estável no celular), segurança de dados e preparação do pacote de lançamento.

## Objetivos
- Reduzir consumo de bateria e aquecimento do processador.
- Implementar criptografia básica na transmissão de vídeo.
- Gerar o pacote final (APK/AAB) otimizado.

## Ferramentas e Pacotes
- **Vulkan API**: Configuração de renderização de alta performance para Android.
- **Occlusion Culling**: Para não renderizar o que não é visível.
- **ASTC Texture Compression**: Para economia de VRAM.

## Scripts a Implementar
1. `BatteryOptimizer.cs`: Ajusta o brilho da tela e frequência de atualização (Refresh Rate) baseado no nível de bateria.
2. `SecureTransmissionManager.cs`: Implementa criptografia simples ou tokens de autenticação via WebSocket.
3. `UpdateChecker.cs`: Verifica versões de software para garantir compatibilidade entre Headset e App.

## Tarefas
- [ ] Otimizar Shaders para mobile (usar FP16 sempre que possível).
- [ ] Configurar Foveated Rendering no lado do headset para economizar GPU.
- [ ] Realizar build final em modo "Release" com as flags de otimização de script IL2CPP.
- [ ] Documentar manual de usuário e especificações de rede mínima.
