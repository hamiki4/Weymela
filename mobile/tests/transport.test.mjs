import assert from 'node:assert/strict';
import { test } from 'node:test';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../v3-transport.js', import.meta.url), 'utf8');
function fixture(native = true) {
  const calls = [], browserCalls = [];
  const scope = {
    URL, Request, Response, Headers, FormData, Blob, Uint8Array, btoa, atob, DOMException,
    location: { origin: 'https://localhost' },
    fetch: async (...args) => { browserCalls.push(args); return new Response('browser'); },
    Capacitor: {
      getPlatform: () => native ? 'android' : 'web',
      nativePromise: async (...args) => { calls.push(args); return { status: 200, headers: { 'Content-Type': 'application/json', 'Set-Cookie': 'private-fixture', 'X-Correlation-ID': 'test' }, data: { ok: true } }; },
    },
  };
  vm.runInNewContext(source, scope);
  scope.WeymelaAndroidTransport.install(scope);
  return { scope, calls, browserCalls };
}
test('maps V3 calls only to the exact Production API', async () => {
  const { scope, calls, browserCalls } = fixture();
  const response = await scope.fetch('/api/session', { headers: { 'X-Weymela-Request': '1' } });
  assert.equal(calls[0][0], 'WeymelaApi');
  assert.equal(calls[0][2].url, 'https://api.weymela.com/api/session');
  assert.equal(browserCalls.length, 0);
  assert.deepEqual(await response.json(), { ok: true });
  assert.equal(response.headers.get('set-cookie'), null);
  assert.equal(response.headers.get('x-correlation-id'), 'test');
});
test('retains profile and idempotency with JSON body', async () => {
  const { scope, calls } = fixture();
  await scope.fetch('/api/session/profile', { method: 'POST', body: '{"profileKey":"customer"}', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': 'repeat-safe', 'X-Weymela-Profile': 'customer' } });
  const options = calls[0][2];
  assert.equal(options.headers['idempotency-key'], 'repeat-safe');
  assert.equal(options.headers['x-weymela-profile'], 'customer');
  assert.equal(options.data, '{"profileKey":"customer"}');
});
test('does not route Firebase or assets through the API bridge', async () => {
  const { scope, calls, browserCalls } = fixture();
  await scope.fetch('https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken');
  await scope.fetch('/icon-180.png');
  assert.equal(calls.length, 0); assert.equal(browserCalls.length, 2);
});
test('web execution is unchanged', async () => {
  const { scope, calls, browserCalls } = fixture(false);
  await scope.fetch('/api/session');
  assert.equal(calls.length, 0); assert.equal(browserCalls.length, 1);
});
test('rejects legacy API, unsupported methods, and complex bodies', async () => {
  const { scope, calls } = fixture();
  await assert.rejects(scope.fetch('/api/v1/auth/refresh'), /Legacy/);
  await assert.rejects(scope.fetch('/api/session', { method: 'DELETE' }), /Unsupported API method/);
  await assert.rejects(scope.fetch('/api/session', { method: 'POST', body: new Blob(['unknown']) }), /Unsupported API request body/);
  assert.equal(calls.length, 0);
});
test('preserves multipart field names, MIME type and bytes', async () => {
  const { scope, calls } = fixture(); const form = new FormData();
  form.append('notes', 'test'); form.append('file', new Blob(['private-fixture'], { type: 'image/png' }), 'photo.png');
  await scope.fetch('/api/creator/photo', { method: 'POST', body: form, headers: { 'Idempotency-Key': 'same-upload' } });
  const payload = calls[0][2];
  assert.equal(payload.dataType, 'formData');
  assert.equal(payload.data[1].type, 'base64File');
  assert.equal(payload.data[1].fileName, 'photo.png');
  assert.equal(payload.data[1].contentType, 'image/png');
  assert.equal(atob(payload.data[1].value), 'private-fixture');
});
test('already aborted calls never enter native transport', async () => {
  const { scope, calls } = fixture(); const controller = new AbortController(); controller.abort();
  await assert.rejects(scope.fetch('/api/session', { signal: controller.signal }), { name: 'AbortError' });
  assert.equal(calls.length, 0);
});
test('private binary responses preserve bytes without cookie headers', async () => {
  const { scope } = fixture();
  scope.Capacitor.nativePromise = async () => ({ status: 200, headers: { 'Content-Type': 'image/png', 'set-cookie': 'private-fixture' }, data: btoa('binary-fixture') });
  const response = await scope.fetch('/api/creator/photo');
  assert.equal(await response.text(), 'binary-fixture');
  assert.equal(response.headers.get('set-cookie'), null);
});
test('204 replies contain no body', async () => {
  const { scope } = fixture();
  scope.Capacitor.nativePromise = async () => ({ status: 204, headers: {}, data: null });
  assert.equal(await (await scope.fetch('/api/session', { method: 'POST' })).text(), '');
});
