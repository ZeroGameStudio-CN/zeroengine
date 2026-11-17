import http from 'node:http';
import fs from 'node:fs';
import crypto from 'node:crypto';

const secret = fs.readFileSync(`${process.env.CREDENTIALS_DIRECTORY}/origin-key`, 'utf8').trim();
const allowed = /^\/(com\.zerogamestudio\.zeroengine\.console(\/.*)?|-\/ping|-\/v1\/search|-\/all)$/;
http.createServer((req, res) => {
  const supplied = Buffer.from(String(req.headers['x-ze-origin-key'] ?? ''));
  const expected = Buffer.from(secret);
  if (supplied.length !== expected.length || !crypto.timingSafeEqual(supplied, expected)) { res.writeHead(404).end(); return; }
  if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405).end(); return; }
  const path = new URL(req.url, 'http://localhost');
  if (!allowed.test(path.pathname)) { res.writeHead(404).end(); return; }
  const upstream = http.request({host: '127.0.0.1', port: 19196, path: path.pathname + path.search, method: req.method,
    headers: {host: 'packages-preview.zerogamestudio.com', 'x-forwarded-proto': 'https'}}, response => {
      res.writeHead(response.statusCode, {'content-type': response.headers['content-type'] ?? 'application/octet-stream'});
      response.pipe(res);
  });
  upstream.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end(); });
  upstream.setTimeout(10000, () => upstream.destroy());
  upstream.end();
}).listen(19473, '127.0.0.1');
