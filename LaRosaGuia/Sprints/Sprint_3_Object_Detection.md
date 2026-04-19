# Sprint 3: Detecção de Objetos com IA (Unity Sentis)

## Visão Geral
Implementação da camada de inteligência que analisa o vídeo capturado para identificar obstáculos, pedestres ou equipamentos industriais em tempo real no dispositivo móvel.

## Objetivos
- Integrar o motor de IA Unity Sentis para inferência no dispositivo.
- Implementar modelo YOLO (v8 ou v11) otimizado para mobile (FP16/Quantizado).
- Extrair metadados dos objetos detectados (Bounding Boxes e Classes).

## Ferramentas e Pacotes
- **Unity Sentis**: Framework oficial da Unity para Deep Learning.
- **YOLOv8-Tiny ONNX**: Modelo de detecção leve e veloz.
- **Compute Shaders**: Para pré-processamento de imagem (redimensionamento e normalização).

## Scripts a Implementar
1. `YoloInferenceManager.cs`: Carrega o modelo ONNX, prepara os buffers de entrada e executa a inferência via GPU/NPU.
2. `DetectionOutputParser.cs`: Decodifica as bounding boxes e realiza Non-Max Suppression (NMS).
3. `ObjectClassifier.cs`: Mapeia as classes detectadas para comportamentos específicos (ex: 'Máquina' = Perigo, 'Pessoa' = Alerta).
4. `DepthEstimator.cs`: (Opcional/Otimizado) Estima a distância monocular dos objetos baseada no tamanho da bounding box e dados AR.

## Tarefas
- [ ] Converter modelo YOLO para formato ONNX compatível com Sentis.
- [ ] Criar pipeline de vídeo-para-sentis usando RenderTextures.
- [ ] Otimizar performance da inferência para rodar em paralelo com o streaming (> 30 FPS).
- [ ] Implementar visualização de debug das bounding boxes na tela AR.
