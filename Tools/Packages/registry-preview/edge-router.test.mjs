import {test} from 'node:test';
import assert from 'node:assert/strict';
import router, {handle} from './edge-router.mjs';
import release from './release-manifest.mjs';

const env = {ORIGIN_URL: 'https://private-origin.example', ORIGIN_KEY: 'test-only-origin-key'};
const core = 'com.zerogamestudio.zeroengine.console';
const coreCatalog = release.packages[core];
function metadata(name = core) {
  const versions = release.packages[name].versions;
  return {name, 'dist-tags': {pilot: Object.keys(versions).at(-1)},
    versions: Object.fromEntries(Object.entries(versions).map(([version, item]) => [version, {
      name, version, description: 'Preview', zgsPilot: {sourceCommit: item.sourceCommit, sourceTree: item.sourceTree},
      dependencies: item.dependencies,
      dist: {integrity: item.sha512, tarball: `${release.publicBase}/${name}/-/${item.asset}`}
    }]))};
}
const response = value => new Response(JSON.stringify(value), {headers: {'Content-Type': 'application/json'}});
const clean = value => JSON.parse(JSON.stringify(value));
const req = (path = `/${core}`, method = 'GET') => new Request(release.publicBase + path, {method});

test('public writes and unknown paths never reach a source', async () => {
  const fetcher = () => assert.fail('A rejected request reached an origin');
  assert.equal((await handle(req(`/${core}`, 'PUT'), env, fetcher)).status, 405);
  assert.equal((await handle(req('/-/user/private'), env, fetcher)).status, 404);
  assert.equal((await handle(req(`/${core}/not-published`), env, fetcher)).status, 404);
  assert.equal((await handle(req(`/${core}.extra`), env, fetcher)).status, 404);
});

test('verified primary metadata keeps URLs and strips client credentials', async () => {
  let count = 0;
  const input = new Request(release.publicBase + '/' + core, {headers: {Authorization: 'client-secret', Cookie: 'client-cookie'}});
  const result = await handle(input, env, async (url, init) => {
    count++; assert.equal(new URL(url).hostname, 'private-origin.example');
    assert.equal(init.headers['X-ZE-Origin-Key'], env.ORIGIN_KEY);
    assert.equal(init.headers.Authorization, undefined); assert.equal(init.headers.Cookie, undefined);
    assert.equal(init.redirect, 'manual');
    return response(metadata());
  });
  assert.equal(count, 1); assert.equal(result.headers.get('X-ZE-Package-Source'), 'worker01');
  assert.deepEqual(await result.json(), clean(metadata()));
});

test('an unreachable primary automatically uses the immutable GitHub metadata', async () => {
  const urls = [];
  const result = await handle(req(), env, async (url, init) => {
    urls.push(url);
    if (urls.length === 1) throw new Error('offline');
    assert.equal(url, coreCatalog.githubBase + coreCatalog.metadataAsset);
    assert.equal(init.headers['X-ZE-Origin-Key'], undefined);
    return response(metadata());
  });
  assert.equal(result.status, 200); assert.equal(result.headers.get('X-ZE-Package-Source'), 'github');
  assert.deepEqual(await result.json(), clean(metadata())); assert.equal(urls.length, 2);
});

test('origin authorization failures do not bypass access control via backup', async () => {
  let count = 0;
  const result = await handle(req(), env, async () => { count++; return new Response('', {status: 403}); });
  assert.equal(result.status, 403); assert.equal(count, 1);
});

test('wrong provenance falls back, and an unverified backup fails closed', async () => {
  const bad = metadata(); bad.versions[Object.keys(coreCatalog.versions)[0]].zgsPilot.sourceCommit = 'invalid';
  let count = 0;
  const recovered = await handle(req(), env, async () => response(++count === 1 ? bad : metadata()));
  assert.equal(recovered.headers.get('X-ZE-Package-Source'), 'github');
  const failed = await handle(req(), env, async () => response(bad));
  assert.equal(failed.status, 503);
});

test('invalid artifact bytes are never delivered from either source', async () => {
  const item = Object.values(coreCatalog.versions)[0]; let count = 0;
  const result = await handle(req(`/${core}/-/${item.asset}`), env,
    async () => { count++; return new Response('corrupt'); });
  assert.equal(result.status, 503); assert.equal(count, 2);
});

test('search, version, all and HEAD remain available on backup without body leakage', async () => {
  const fetcher = async url => {
    if (url.startsWith(env.ORIGIN_URL)) return new Response('', {status: 503});
    const name = Object.keys(release.packages).find(name => {
      const catalog = release.packages[name];
      return url === catalog.githubBase + catalog.metadataAsset;
    });
    return response(metadata(name));
  };
  const search = await handle(req('/-/v1/search?text=anything'), env, fetcher);
  assert.deepEqual((await search.json()).objects.map(item => item.package.name), Object.keys(release.packages));
  const version = Object.keys(coreCatalog.versions)[0];
  assert.equal((await (await handle(req(`/${core}/${version}`), env, fetcher)).json()).version, version);
  assert.deepEqual(Object.keys(await (await handle(req('/-/all'), env, fetcher)).json()).slice(1), Object.keys(release.packages));
  const head = await handle(req('/-/ping', 'HEAD'), env, fetcher);
  assert.equal(head.status, 200); assert.equal(await head.text(), '');
});

test('a frontend with mismatched dependency metadata is never served', async () => {
  const name = core + '.quantum';
  const bad = metadata(name);
  Object.values(bad.versions)[0].dependencies = {[core]: 'wrong-version'};
  const result = await handle(req(`/${name}`), env, async () => response(bad));
  assert.equal(result.status, 503);
});

test('Cloudflare entry ignores ExecutionContext as a fetch implementation', async () => {
  const original = globalThis.fetch;
  globalThis.fetch = async () => response(metadata());
  try { assert.equal((await router.fetch(req(), env, {waitUntil() {}})).status, 200); }
  finally { globalThis.fetch = original; }
});
