import io
import http.server
import socketserver
import urllib.parse
import wave

# ======================================================================
# Servidor Piper TTS ONNX — Corrigido para usar a API Python correta
# Dependências: pip install piper-tts
# Uso: python Run_Piper_Server.py
# ======================================================================

MODEL_ONNX = "es_MX-ald-medium.onnx"
MODEL_JSON  = "es_MX-ald-medium.onnx.json"
PORT        = 5000

# Carrega o modelo UMA vez na inicialização (evita recarregar a cada request)
_voice = None

def get_voice():
    global _voice
    if _voice is None:
        try:
            from piper import PiperVoice
            print(f"[Piper] Carregando modelo '{MODEL_ONNX}'...")
            _voice = PiperVoice.load(MODEL_ONNX, config_path=MODEL_JSON, use_cuda=False)
            print("[Piper] Modelo ONNX carregado com sucesso!")
        except Exception as e:
            print(f"[Piper] ERRO ao carregar modelo: {e}")
            print("[Piper] Verifique se 'piper-tts' está instalado: pip install piper-tts")
    return _voice

class PiperTTSHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        # Suprime logs HTTP repetitivos do servidor
        print(f"[HTTP] {args[0]} {args[1]}")

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        params = urllib.parse.parse_qs(parsed.query)
        text   = params.get("text", [""])[0].strip()

        if not text:
            self._send_error(400, "Parâmetro 'text' ausente ou vazio.")
            return

        print(f"[Piper] Sintetizando: '{text}'")

        voice = get_voice()
        if voice is None:
            self._send_error(500, "Modelo Piper não carregado.")
            return

        try:
            # Sintetiza diretamente para um buffer em memória (sem arquivo temporário)
            wav_buffer = io.BytesIO()

            with wave.open(wav_buffer, "wb") as wav_file:
                voice.synthesize(text, wav_file)

            wav_bytes = wav_buffer.getvalue()
            wav_buffer.close()

            if len(wav_bytes) == 0:
                self._send_error(500, "Síntese retornou áudio vazio.")
                return

            print(f"[Piper] Áudio gerado: {len(wav_bytes)} bytes")

            # Envia com Content-Type correto para o Unity reconhecer como WAV
            self.send_response(200)
            self.send_header("Content-Type",   "audio/wav")
            self.send_header("Content-Length", str(len(wav_bytes)))
            # CORS para debug local
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(wav_bytes)

        except Exception as e:
            print(f"[Piper] ERRO na síntese: {e}")
            self._send_error(500, str(e))

    def _send_error(self, code, message):
        body = message.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type",   "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    # Pré-aquece o modelo na inicialização
    get_voice()

    print(f"\n{'='*60}")
    print(f"  Piper ONNX TTS Server - es_MX (voz infantil masculina)")
    print(f"  Porta: {PORT}  |  Endpoint: http://0.0.0.0:{PORT}/?text=...")
    print(f"  Acesso pelo celular: http://192.168.43.10:{PORT}/?text=Hola")
    print(f"{'='*60}\n")

    with socketserver.TCPServer(("", PORT), PiperTTSHandler) as httpd:
        httpd.serve_forever()
