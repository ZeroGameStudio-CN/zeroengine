import release from './release-manifest.mjs';

const MAX_METADATA = 512 * 1024;
function route(path) {
  if (path === `/${release.name}` || path === `/${release.name}/`) return {kind: 'metadata'};
  for (const [version, item] of Object.entries(release.versions)) {
    if (path === `/${release.name}/${version}`) return {kind: 'version', version};
    if (path === `/${release.name}/-/${item.asset}`) return {kind: 'tarball', item};
  }
  if (path === '/-/ping') return {kind: 'ping'};
  if (path === '/-/all') return {kind: 'all'};
  if (path === '/-/v1/search') return {kind: 'search'};
  return null;
}

async function bytes(response, maximum) {
  const reader = response.body?.getReader();
  if (!reader) return new Uint8Array();
  const chunks = []; let size = 0;
  try {
    for (;;) {
      const {done, value} = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > maximum) throw new Error('response-too-large');
      chunks.push(value);
    }
  } catch (error) { await reader.cancel(); throw error; }
  const result = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.byteLength; }
  return result;
}

function validateMetadata(value) {
  if (value?.name !== release.name || !value.versions) throw new Error('invalid-metadata');
  for (const [version, expected] of Object.entries(release.versions)) {
    const actual = value.versions[version];
    if (!actual || actual.version !== version || actual.zgsPilot?.sourceCommit !== expected.sourceCommit ||
        actual.dist?.integrity !== expected.sha512 ||
        actual.dist?.tarball !== `${release.publicBase}/${release.name}/-/${expected.asset}`)
      throw new Error('metadata-provenance-mismatch');
  }
  // A new version is routable only after its immutable backup has been admitted.
  value.versions = Object.fromEntries(Object.keys(release.versions).map(version => [version, value.versions[version]]));
  value['dist-tags'] = Object.fromEntries(Object.entries(value['dist-tags'] ?? {}).filter(([, version]) => version in release.versions));
  return value;
}

function json(value) { return new TextEncoder().encode(JSON.stringify(value)); }
function buildMetadata(selected, metadata) {
  if (selected.kind === 'version') return json(metadata.versions[selected.version]);
  if (selected.kind === 'ping') return json({});
  if (selected.kind === 'all') return json({_updated: 0, [release.name]: metadata});
  if (selected.kind === 'search') {
    const version = Object.keys(release.versions).at(-1);
    return json({objects: [{package: {name: release.name, version, description: metadata.versions[version].description,
      links: {npm: `${release.publicBase}/${release.name}`}}}], total: 1, time: '2026-11-17T00:00:00.000Z'});
  }
  return json(metadata);
}

async function request(fetcher, url, headers, timeout) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeout);
  try {
    const response = await fetcher(url, {method: 'GET', headers, signal: controller.signal, redirect: 'follow'});
    return {response, done: () => clearTimeout(timer)};
  } catch (error) { clearTimeout(timer); throw error; }
}

async function verifyTarball(response, item) {
  const data = await bytes(response, item.bytes);
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', data));
  const hash = Array.from(digest, value => value.toString(16).padStart(2, '0')).join('');
  if (data.byteLength !== item.bytes || hash !== item.sha256) throw new Error('tarball-integrity-mismatch');
  return data;
}

export async function handle(requestInput, env, fetcher = fetch) {
  if (!['GET', 'HEAD'].includes(requestInput.method)) return new Response('Method not allowed', {status: 405});
  const incoming = new URL(requestInput.url);
  const selected = route(incoming.pathname);
  if (!selected) return new Response('Not found', {status: 404});
  let data, source;
  try {
    const path = selected.kind === 'tarball' ? incoming.pathname : `/${release.name}`;
    const operation = await request(fetcher, new URL(path, env.ORIGIN_URL).href,
      {'X-ZE-Origin-Key': env.ORIGIN_KEY, 'Accept': selected.kind === 'tarball' ? 'application/octet-stream' : 'application/json'}, 3000);
    try {
    const origin = operation.response;
    if (origin.status === 401 || origin.status === 403)
      return new Response('Origin access denied', {status: 403, headers: {'Cache-Control': 'no-store'}});
    if (!origin.ok) throw new Error('origin-unavailable');
    if (selected.kind === 'tarball') data = await verifyTarball(origin, selected.item);
    else data = buildMetadata(selected, validateMetadata(JSON.parse(new TextDecoder().decode(await bytes(origin, MAX_METADATA)))));
    source = 'worker01';
    } finally { operation.done(); }
  } catch {
    try {
      const operation = await request(fetcher, release.githubBase + (selected.kind === 'tarball' ? selected.item.asset : 'registry.json'),
        {'Accept': 'application/octet-stream'}, 8000);
      try {
      const backup = operation.response;
      if (!backup.ok) throw new Error('backup-unavailable');
      if (selected.kind === 'tarball') data = await verifyTarball(backup, selected.item);
      else data = buildMetadata(selected, validateMetadata(JSON.parse(new TextDecoder().decode(await bytes(backup, MAX_METADATA)))));
      source = 'github';
      } finally { operation.done(); }
    } catch { return new Response('Verified package source unavailable', {status: 503, headers: {'Cache-Control': 'no-store'}}); }
  }
  const headers = {'Content-Type': selected.kind === 'tarball' ? 'application/octet-stream' : 'application/json',
    'Content-Length': String(data.byteLength), 'X-Content-Type-Options': 'nosniff', 'X-ZE-Package-Source': source,
    'Cache-Control': selected.kind === 'tarball' ? 'public, max-age=31536000, immutable' : 'no-store'};
  if (selected.kind === 'tarball') headers.ETag = `"${selected.item.sha256}"`;
  return new Response(requestInput.method === 'HEAD' ? null : data, {headers});
}

export default {fetch(request, env) { return handle(request, env); }};
