import argparse
import http.server
import socketserver
import subprocess
import urllib.parse
import os

# Servidor Portátil do Piper ONNX para Unity
# Este script levanta um servidor HTTP rápido que roda o motor do Piper 
# localmente usando o modelo ONNX (voz menino/espanhol)

MODEL_FILE = "es_MX-ald-medium.onnx"

class PiperTTSHandler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        parsed_path = urllib.parse.urlparse(self.path)
        if parsed_path.path == '/':
            query = urllib.parse.parse_qs(parsed_path.query)
            text = query.get('text', [''])[0]
            
            if text:
                print(f"[Piper ONNX] Gerando áudio para: {text}")
                
                output_wav = "output_temp.wav"
                
                # Executa o piper diretamente com os textos convertendo para wav
                # No windows, se usar exe: piper.exe
                # No Linux/Docker/Python: piper
                cmd = f'echo "{text}" | piper -m {MODEL_FILE} -f {output_wav}'
                
                # Usar shell=True resolve piping no python dependendo do S.O.
                subprocess.run(cmd, shell=True, check=False)
                
                if os.path.exists(output_wav):
                    with open(output_wav, 'rb') as f:
                        wav_data = f.read()
                        
                    self.send_response(200)
                    self.send_header('Content-Type', 'audio/wav')
                    self.send_header('Content-Length', str(len(wav_data)))
                    self.end_headers()
                    self.wfile.write(wav_data)
                    return
                else:
                    self.send_response(500)
                    self.end_headers()
                    self.wfile.write(b"Erro interno na criacao do audio Piper.")
                    return
        
        self.send_response(404)
        self.end_headers()

if __name__ == "__main__":
    PORT = 5000
    print(f"==========================================================")
    print(f" Inciando motor Neural Piper TTS - Modelo Espanhol (ONNX) ")
    print(f" Operando na porta {PORT} em conjunto com LLaMA")
    print(f" Lembre-se de rodar primeiro: pip install piper-tts")
    print(f"==========================================================")
    
    with socketserver.TCPServer(("", PORT), PiperTTSHandler) as httpd:
        httpd.serve_forever()
