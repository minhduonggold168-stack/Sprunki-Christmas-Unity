import http.server
import socketserver
import os
import sys

PORT = 8080
DIRECTORY = r"D:\Sprunki_Unity\My project"

class CustomHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIRECTORY, **kwargs)

    def end_headers(self):
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Cache-Control', 'no-store, must-revalidate')
        super().end_headers()

if __name__ == '__main__':
    os.chdir(DIRECTORY)
    with socketserver.TCPServer(("", PORT), CustomHandler) as httpd:
        print(f"Sprunki Christmas Web Server serving at http://localhost:{PORT}/web/ on port {PORT}")
        sys.stdout.flush()
        httpd.serve_forever()
