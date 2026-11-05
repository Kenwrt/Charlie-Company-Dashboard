"""Loopback-only compatibility fix for agent-browser 0.26.0 dashboard key codes.

Retains the official dashboard and its WebSocket/origin protections. Only its known
static keyboard-mapping expression is changed. Login keystrokes go directly through
the original session WebSocket, never through this HTTP asset proxy or its logs.
"""
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import re
from urllib.parse import urlsplit

UPSTREAM_PORT = 19234
LISTEN_PORT = 19235
BROKEN = 'i=r?.keyCode??(1===e.key.length?e.key.charCodeAt(0):0)'
FIXED = ('i=r?.keyCode??({Period:190,Comma:188,Semicolon:186,Quote:222,'
         'BracketLeft:219,BracketRight:221,Backslash:220,Slash:191,Backquote:192,'
         'Minus:189,Equal:187,Space:32,NumpadDecimal:110}[e.code]??'
         '(/^Key[A-Z]$/.test(e.code)?e.code.charCodeAt(3):'
         '/^Digit[0-9]$/.test(e.code)?e.code.charCodeAt(5):'
         '/^Numpad[0-9]$/.test(e.code)?96+Number(e.code.slice(6)):0))')
HOP_HEADERS = {'connection', 'keep-alive', 'proxy-authenticate', 'proxy-authorization',
               'te', 'trailer', 'transfer-encoding', 'upgrade'}


def static_get(path):
    connection = http.client.HTTPConnection('127.0.0.1', UPSTREAM_PORT, timeout=15)
    try:
        connection.request('GET', path, headers={'Accept-Encoding': 'identity'})
        response = connection.getresponse()
        if response.status != 200:
            raise RuntimeError('Native viewer static source unavailable')
        return response.read()
    finally:
        connection.close()


def prepare_patch():
    html = static_get('/').decode('utf-8')
    matches = []
    for path in re.findall(r'<script[^>]+src="([^"]+)"', html):
        if not path.startswith('/') or path.startswith('//'):
            continue
        source = static_get(path).decode('utf-8')
        if BROKEN in source:
            if source.count(BROKEN) != 1:
                raise RuntimeError('Keyboard source changed; no guessed patch was applied')
            matches.append((urlsplit(path).path, source.replace(BROKEN, FIXED).encode('utf-8')))
    if len(matches) != 1:
        raise RuntimeError('Expected one known keyboard mapper; no guessed patch was applied')
    return dict(matches)


class ViewerHandler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.0'
    server_version = 'HomeDepotPrivateViewer'

    def log_message(self, *args):
        pass  # Do not log request values, bodies, cookies or input events.

    def forward(self):
        if not self.path.startswith('/') or self.path.startswith('//'):
            self.send_error(400)
            return
        if self.command in ('GET', 'HEAD') and self.path == '/__keyboard_check':
            # Local-only, non-submitting input for a focused forwarding check. Never
            # exercise diagnostic typing in the owner's actual credential fields.
            data = b'<!doctype html><html><head><title>Temporary keyboard check</title></head><body style="font:24px system-ui;padding:60px"><h1>Temporary keyboard check</h1><label>Non-secret test characters <input aria-label="Non-secret test characters" style="font:32px monospace;width:400px" autofocus></label><p>No form, no submission, no logging.</p></body></html>'
            self.send_response(200)
            self.send_header('Content-Type', 'text/html; charset=utf-8')
            self.send_header('Content-Length', str(len(data)))
            self.send_header('Cache-Control', 'no-store')
            self.end_headers()
            if self.command != 'HEAD':
                self.wfile.write(data)
            return
        if self.command in ('GET', 'HEAD') and urlsplit(self.path).path in self.server.patched_assets:
            data = self.server.patched_assets[urlsplit(self.path).path]
            self.send_response(200)
            self.send_header('Content-Type', 'application/javascript; charset=utf-8')
            self.send_header('Content-Length', str(len(data)))
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.end_headers()
            if self.command != 'HEAD':
                self.wfile.write(data)
            return
        connection = http.client.HTTPConnection('127.0.0.1', UPSTREAM_PORT, timeout=60)
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if length < 0 or length > 2 * 1024 * 1024:
                self.send_error(413)
                return
            body = self.rfile.read(length) if length else None
            # Keep Origin, authorization and security headers intact. No origin spoofing,
            # authorization exceptions, browser-session extraction or TLS changes.
            headers = {name: value for name, value in self.headers.items()
                       if name.lower() not in HOP_HEADERS | {'accept-encoding', 'if-none-match', 'if-modified-since'}}
            headers['Accept-Encoding'] = 'identity'
            connection.request(self.command, self.path, body=body, headers=headers)
            response = connection.getresponse()
            self.send_response(response.status)
            for name, value in response.getheaders():
                if name.lower() not in HOP_HEADERS | {'cache-control', 'etag', 'last-modified'}:
                    self.send_header(name, value)
            self.send_header('Cache-Control', 'no-store')
            self.end_headers()
            if self.command != 'HEAD':
                while chunk := response.read1(65536):
                    self.wfile.write(chunk)
                    self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError):
            pass
        finally:
            connection.close()

    do_GET = forward
    do_HEAD = forward
    do_POST = forward
    do_PUT = forward
    do_PATCH = forward
    do_DELETE = forward
    do_OPTIONS = forward


if __name__ == '__main__':
    assets = prepare_patch()
    server = ThreadingHTTPServer(('127.0.0.1', LISTEN_PORT), ViewerHandler)
    server.daemon_threads = True
    server.patched_assets = assets
    print(json.dumps({'stage': 'keyboard-compatibility-ready', 'bind': '127.0.0.1',
                      'port': LISTEN_PORT, 'patched_static_assets': len(assets)}), flush=True)
    server.serve_forever()
