import release from './release-manifest.mjs';

const MAX_METADATA = 512 * 1024;
function route(path) {
  for (const [name, catalog] of Object.entries(release.packages)) {
    if (path === `/${name}` || path === `/${name}/`) return {kind: 'metadata', name};
    for (const [version, item] of Object.entries(catalog.versions)) {
      if (path === `/${name}/${version}`) return {kind: 'version', name, version};
      if (path === `/${name}/-/${item.asset}`) return {kind: 'tarball', name, item};
    }
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

function validateMetadata(value, name, versions) {
  if (value?.name !== name || !value.versions) throw new Error('invalid-metadata');
  for (const [version, expected] of Object.entries(versions)) {
    const actual = value.versions[version];
    if (actual?.name !== name || actual.version !== version || actual.zgsPilot?.sourceCommit !== expected.sourceCommit ||
        actual.dist?.integrity !== expected.sha512 ||
        actual.dist?.tarball !== `${release.publicBase}/${name}/-/${expected.asset}`)
      throw new Error('metadata-provenance-mismatch');
    if (expected.sourceTree && actual.zgsPilot?.sourceTree !== expected.sourceTree)
      throw new Error('metadata-tree-mismatch');
    if (expected.dependencies) {
      const dependencies = Object.entries(actual.dependencies ?? {});
      if (dependencies.length !== Object.keys(expected.dependencies).length ||
          dependencies.some(([dependency, version]) => expected.dependencies[dependency] !== version))
        throw new Error('metadata-dependency-mismatch');
    }
  }
  // A new version is routable only after its immutable backup has been admitted.
  value.versions = Object.fromEntries(Object.keys(versions).map(version => [version, value.versions[version]]));
  value['dist-tags'] = Object.fromEntries(Object.entries(value['dist-tags'] ?? {}).filter(([, version]) => Object.hasOwn(versions, version)));
  return value;
}

function json(value) { return new TextEncoder().encode(JSON.stringify(value)); }
async function request(fetcher, url, headers, timeout, redirect) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeout);
  try {
    const response = await fetcher(url, {method: 'GET', headers, signal: controller.signal, redirect});
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

async function readPackage(selected, env, fetcher) {
  const {name, item} = selected;
  const catalog = release.packages[name];
  const path = item ? `/${name}/-/${item.asset}` : `/${name}`;
  async function decode(response) {
    if (!response.ok) throw new Error('source-unavailable');
    if (item) return verifyTarball(response, item);
    return validateMetadata(JSON.parse(new TextDecoder().decode(await bytes(response, MAX_METADATA))), name, catalog.versions);
  }
  try {
    const operation = await request(fetcher, new URL(path, env.ORIGIN_URL).href,
      {'X-ZE-Origin-Key': env.ORIGIN_KEY, 'Accept': item ? 'application/octet-stream' : 'application/json'}, 3000, 'manual');
    try {
    const origin = operation.response;
      if (origin.status === 401 || origin.status === 403) return {status: 403};
      return {data: await decode(origin), source: 'worker01'};
    } finally { operation.done(); }
  } catch {
    try {
      const operation = await request(fetcher, catalog.githubBase + (item?.asset ?? catalog.metadataAsset),
        {'Accept': 'application/octet-stream'}, 8000, 'follow');
      try {
        return {data: await decode(operation.response), source: 'github'};
      } finally { operation.done(); }
    } catch { return {status: 503}; }
  }
}

function buildIndex(kind, names, results) {
  if (kind === 'ping') return {};
  if (kind === 'all') return {_updated: 0, ...Object.fromEntries(names.map((name, i) => [name, results[i].data]))};
  const objects = names.map((name, i) => {
    const version = Object.keys(release.packages[name].versions).at(-1);
    return {package: {name, version, description: results[i].data.versions[version].description,
      links: {npm: `${release.publicBase}/${name}`}}};
  });
  return {objects, total: objects.length, time: '2026-11-17T00:00:00.000Z'};
}

export async function handle(requestInput, env, fetcher = fetch) {
  if (!['GET', 'HEAD'].includes(requestInput.method)) return new Response('Method not allowed', {status: 405});
  const selected = route(new URL(requestInput.url).pathname);
  if (!selected) return new Response('Not found', {status: 404});
  const names = selected.name ? [selected.name] : Object.keys(release.packages);
  if (selected.kind === 'ping') names.splice(1);
  const results = await Promise.all(names.map(name => readPackage({...selected, name}, env, fetcher)));
  if (results.some(result => result.status === 403))
    return new Response('Origin access denied', {status: 403, headers: {'Cache-Control': 'no-store'}});
  if (results.some(result => result.status))
    return new Response('Verified package source unavailable', {status: 503, headers: {'Cache-Control': 'no-store'}});
  const first = results[0];
  const source = results.every(result => result.source === first.source) ? first.source : 'mixed';
  const data = selected.item ? first.data : json(selected.kind === 'metadata' ? first.data :
    selected.kind === 'version' ? first.data.versions[selected.version] : buildIndex(selected.kind, names, results));
  const headers = {'Content-Type': selected.kind === 'tarball' ? 'application/octet-stream' : 'application/json',
    'Content-Length': String(data.byteLength), 'X-Content-Type-Options': 'nosniff', 'X-ZE-Package-Source': source,
    'Cache-Control': selected.kind === 'tarball' ? 'public, max-age=31536000, immutable' : 'no-store'};
  if (selected.kind === 'tarball') headers.ETag = `"${selected.item.sha256}"`;
  return new Response(requestInput.method === 'HEAD' ? null : data, {headers});
}

export default {fetch(request, env) { return handle(request, env); }};
