# Sprint 2: Fundamentos Espaciais e Fusão de Sensores

## Visão Geral
Integração do AR Foundation para permitir que o dispositivo móvel entenda o espaço físico enquanto recebe o vídeo do headset, sincronizando os sistemas de coordenadas entre VR e AR.

## Objetivos
- Configurar AR Foundation (ARCore) no dispositivo Android.
- Implementar SLAM (Simultaneous Localization and Mapping) robusto.
- Fundir dados de IMU do celular para predição de pose suave.

## Ferramentas e Pacotes
- **AR Foundation**: Core da experiência AR.
- **ARCore Extensions**: Recursos avançados de profundidade e geolocalização.
- **Input System**: Para capturar dados brutos do acelerômetro e giroscópio (IMU).

## Scripts a Implementar
1. `ARCoordinator.cs`: Inicializa e gerencia a sessão AR, garantindo que o plano de chão seja detectado.
2. `ImuFusionProvider.cs`: Lê dados brutos do giroscópio/acelerômetro para complementar o rastreamento visual do ARCore.
3. `SpatialSyncManager.cs`: Algoritmo para sincronizar o "Zero Espacial" do headset VR com o "Zero Espacial" da sessão AR no celular.
4. `PointTracker.cs`: Identifica e rastreia pontos de interesse no mundo real para ancoragem do conteúdo digital.

## Tarefas
- [ ] Validar rastreamento de planos (Piso e Paredes).
- [ ] Implementar filtro de Kalman ou similar para suavização de IMU.
- [ ] Criar sistema de calibração manual/automática para sobrepor o vídeo do headset no mundo real.
- [ ] Testar estabilidade do rastreamento em diferentes condições de iluminação.
