#!/usr/bin/env python3
"""Real two-proxy upload regression against an owned disposable BrowserHost.

Requires the browser-build outputs, Docker and .NET (or --runner for a local
SDK container). Never accepts a live URL/database. No financial writes enabled.
"""
import argparse
import http.cookiejar
import json
import os
import pathlib
import socket
import struct
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
IMAGE = 'nginx:stable-alpine@sha256:dc5069ad14f19660b141b21236140b91656bf89bbc3e2417c70ae650cd66104c'


def port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def png(size):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    # A valid 1x1 RGBA PNG, padded with a valid ancillary text chunk.
    header = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 6, 0, 0, 0))
    pixels = chunk(b'IDAT', zlib.compress(b'\0\xff\0\0\xff'))
    end = chunk(b'IEND', b'')
    return header + pixels + chunk(b'tEXt', b'Comment\0' + b'x' * (size - len(header + pixels + end) - 20)) + end


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runner', help='Owned local SDK container; omitted on hosted CI')
    parser.add_argument('--node-runner', help='Optional owned Node container when the SDK runner has no Node binary')
    args = parser.parse_args()
    control = ROOT / '.artifacts/browser-host.json'
    if control.exists():
        raise RuntimeError('Refusing to use an existing BrowserHost')
    host = None
    container = 'weymela-image-proxy-test-' + os.urandom(6).hex()
    with tempfile.TemporaryDirectory(prefix='image-proxy-', dir=ROOT / '.artifacts') as temporary:
        temp = pathlib.Path(temporary)
        temp.chmod(0o755)
        receipts = temp / 'receipts'
        receipts.mkdir(mode=0o700)
        web_port, edge_port = port(), port()
        env = {**os.environ, 'V3__AllowedOrigins__0': f'http://127.0.0.1:{edge_port}', 'V3_SOURCE_ROOT': str(ROOT), 'V3__FinancialWritesEnabled': 'false',
               'V3__Deposits__Mode': 'ManualApproval', 'V3__Deposits__ReceiptDirectory': str(receipts)}
        command = ['dotnet', 'tests/Weymela.BrowserHost/bin/Release/net10.0/Weymela.BrowserHost.dll']
        if args.runner:
            command = ['docker', 'exec', *sum((['-e', key] for key in
                ('V3__AllowedOrigins__0', 'V3_SOURCE_ROOT', 'V3__FinancialWritesEnabled', 'V3__Deposits__Mode', 'V3__Deposits__ReceiptDirectory')), []), args.runner, *command]
        with (temp / 'host.log').open('w') as log:
            try:
                host = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
                deadline = time.monotonic() + 180
                while not control.exists():
                    if host.poll() is not None or time.monotonic() > deadline:
                        raise RuntimeError('Disposable BrowserHost did not start')
                    time.sleep(.25)
                identity = json.loads(control.read_text())
                assert identity['url'].startswith('http://127.0.0.1:')
                web = (ROOT / 'docker/web/nginx.conf').read_text().replace('listen 8080;', f'listen 127.0.0.1:{web_port};').replace('http://api:8080', identity['url'])
                edge = (ROOT / 'docs/deployment/pilot-image-uploads.nginx.conf').read_text().replace('127.0.0.1:18080', f'127.0.0.1:{web_port}')
                # Same production HTTP routing; TLS is outside this isolated loopback fixture.
                edge_server = f'''server {{ listen 127.0.0.1:{edge_port};
                    proxy_set_header Host $host;
                    proxy_set_header X-Forwarded-Proto http;
                    proxy_set_header X-Forwarded-For $remote_addr;
                    {edge}
                    location / {{ proxy_pass http://127.0.0.1:{web_port}; }}
                }}'''
                pos = web.rfind('}')
                (temp / 'nginx.conf').write_text(web[:pos] + edge_server + web[pos:])
                (temp / 'proxy.conf').write_text((ROOT / 'docker/web/api-proxy.conf').read_text()
                    .replace('http://api:8080', identity['url']))
                command = ['docker', 'run', '-d', '--name', container, '--network', 'host', '--read-only',
                    '--user', '101:101', '--cap-drop', 'ALL', '--tmpfs', '/tmp:rw,size=64m',
                    '-v', f'{temp}/nginx.conf:/etc/nginx/nginx.conf:ro',
                    '-v', f'{temp}/proxy.conf:/etc/nginx/weymela-api-proxy.conf:ro',
                    '-v', f'{ROOT}/docker/web/security-headers.conf:/etc/nginx/weymela-security-headers.conf:ro',
                    '-v', f'{ROOT}/src/Weymela.Web/dist:/usr/share/nginx/html:ro',
                    '--entrypoint', 'nginx', IMAGE, '-g', 'daemon off;']
                subprocess.run(command, check=True, stdout=subprocess.DEVNULL)
                opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
                base = f'http://127.0.0.1:{edge_port}'
                def request(path, body=None, content='application/json'):
                    req = urllib.request.Request(base + path, data=body,
                        headers={'Content-Type': content, 'X-Weymela-Request': '1', 'Origin': base})
                    try:
                        with opener.open(req, timeout=25) as response:
                            return response.status, response.read()
                    except urllib.error.HTTPError as error:
                        return error.code, error.read()
                deadline = time.monotonic() + 20
                while True:
                    try:
                        if request('/health/ready')[0] == 200: break
                    except OSError: pass
                    if time.monotonic() > deadline: raise RuntimeError('Test proxy not ready')
                    time.sleep(.2)
                status, _ = request('/api/development/session', json.dumps({'alias': 'business', 'accessKey': identity['accessKey']}).encode())
                assert status == 204, status
                def upload(size):
                    body = b'--fixture\r\nContent-Disposition: form-data; name="amount"\r\n\r\n3000\r\n--fixture\r\nContent-Disposition: form-data; name="receipt"; filename="receipt.png"\r\nContent-Type: image/png\r\n\r\n' + png(size) + b'\r\n--fixture--\r\n'
                    return request('/api/business/deposit-requests', body, 'multipart/form-data; boundary=fixture')
                for size in (100, 65752, 4 * 1024 * 1024):
                    status, body = upload(size)
                    assert status == 503 and json.loads(body)['code'] == 'FinancialWritesPaused', (size, status)
                    print(f'Two proxies -> real API: {size} bytes -> FinancialWritesPaused PASS', flush=True)
                status, body = upload(5 * 1024 * 1024 + 1)
                assert status == 413 and json.loads(body)['code'] == 'RequestTooLarge'
                assert request('/api/business/deposit-requests')[1] == b'[]'
                assert not list(receipts.iterdir())
                status, _ = request('/api/development/session', json.dumps({'alias': 'creator', 'accessKey': identity['accessKey']}).encode())
                assert status == 204, status
                review = b'--fixture\r\nContent-Disposition: form-data; name="media"; filename="review.mp4"\r\nContent-Type: video/mp4\r\n\r\n' + b'x' * 65752 + b'\r\n--fixture--\r\n'
                status, body = request('/api/creator/creator-budgets/11111111-1111-1111-1111-111111111111/content/review', review, 'multipart/form-data; boundary=fixture')
                assert status != 413, (status, body)
                assert request('/api/session/switch-profile', b'x' * 32769)[0] == 413
                print('Bounded JSON 413, review upload route, ordinary 32 KiB limit, no receipt/deposit persisted PASS', flush=True)
                fixture = temp / 'receipt.png'
                fixture.write_bytes(png(65752))
                command = ['node', 'src/Weymela.Web/scripts/test-upload-proxy.mjs', base, str(fixture)]
                if args.node_runner or args.runner:
                    command = ['docker', 'exec', args.node_runner or args.runner, *command]
                subprocess.run(command, check=True, cwd=ROOT, timeout=90)
            finally:
                subprocess.run(['docker', 'rm', '-f', container], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                if host is not None and host.poll() is None:
                    if args.runner:
                        subprocess.run(['docker', 'exec', args.runner, 'pkill', '-TERM', '-f', '^dotnet tests/Weymela.BrowserHost/'], check=False)
                    else: host.terminate()
                    host.wait(timeout=30)
                control.unlink(missing_ok=True)


if __name__ == '__main__':
    main()
