@echo off
title Piper TTS ONNX Server — es_MX (voz infantil)
color 0A
echo.
echo ============================================================
echo   PIPER TTS ONNX — Servidor de Voz (Espanhol Infantil MX)
echo ============================================================
echo.

:: Vai para a pasta onde este .bat está (junto ao .onnx e ao .py)
cd /d "%~dp0"

echo [1/3] Verificando Python...
python --version >nul 2>&1
if errorlevel 1 (
    echo [ERRO] Python nao encontrado. Instale em https://python.org
    pause
    exit /b 1
)
python --version

echo.
echo [2/3] Verificando piper-tts...
python -c "import piper" >nul 2>&1
if errorlevel 1 (
    echo [INFO] piper-tts nao instalado. Instalando agora...
    pip install piper-tts
    if errorlevel 1 (
        echo [ERRO] Falha ao instalar piper-tts.
        pause
        exit /b 1
    )
) else (
    echo [OK] piper-tts encontrado.
)

echo.
echo [3/3] Verificando modelo ONNX...
if not exist "es_MX-ald-medium.onnx" (
    echo [ERRO] Modelo es_MX-ald-medium.onnx nao encontrado nesta pasta!
    echo        Baixe em: https://huggingface.co/rhasspy/piper-voices
    pause
    exit /b 1
)
echo [OK] Modelo encontrado.

echo.
echo ============================================================
echo   Iniciando servidor na porta 5000...
echo   IP do notebook na rede:
ipconfig | findstr /i "IPv4"
echo.
echo   No Unity, confirme que piperEndpointUrl aponta para
echo   o IP acima com porta 5000.
echo ============================================================
echo.

python Run_Piper_Server.py

pause
