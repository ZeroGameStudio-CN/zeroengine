# ZE package distribution Preview

The feed serves the Console Core, UI, and Quantum bridge at
`https://packages-preview.zerogamestudio.com`. The POB transport Preview uses
the same three package trees as the project's Git dependencies.

## Read path

```text
Unity / npm -> one Cloudflare Worker hostname
             -> Worker01 Tunnel -> read-only origin gate -> Verdaccio
             -> immutable GitHub release backup when the origin is unavailable
```

The public hostname stays the same during fallback and recovery. Metadata keeps
tarball URLs on that hostname. The edge admits only versions in
[`release-manifest.mjs`](release-manifest.mjs), verifies source commit, integrity,
size and SHA-256, and labels package responses `X-ZE-Package-Source: worker01|github`.
The search index reports `mixed` if its package entries came from both sources.
Network errors, timeouts, unavailable responses and invalid artifacts select the
backup; origin authorization failures stay closed. Both invalid sources return
503. New versions become visible only after their backup and manifest are ready.

Anonymous GET/HEAD supports the three listed packages, versioned tarballs, search and
ping. Public writes, account routes and unrelated packages are rejected. The
origin gate requires a private header supplied by the edge; client credentials
and cookies are never forwarded. Cloudflare remains a shared entry dependency;
GitHub fallback does not cover a Cloudflare outage or guarantee every network.

The project manifest carries the scoped registry and exact version; its lock
file carries registry provenance. Teammates receive those project files through
SCM. They need no personal npm/UPM/Git settings or GitHub access for these package
downloads. The [minimal project](sample/README.md) shows the Core-only trial.

## Maintainer deployment

Worker01 owns the runtime and state. Shanghai is not in the current read path.
Verdaccio 7.1.1 and Node 24.11.1 use the pinned npm lock and a private Node release;
no global Node upgrade, npm uplink, web UI, or shared Caddy/Tunnel change is needed.

| Component | Boundary |
| --- | --- |
| `zgs-ze-upm-preview.service` | Loopback `19196`, DynamicUser, 256 MiB / 50% CPU |
| `zgs-ze-upm-origin-gate.service` | Loopback `19473`, systemd origin-key credential, 96 MiB / 25% CPU |
| `zgs-ze-upm-tunnel.service` | Dedicated remote-managed Tunnel and token credential, 128 MiB / 25% CPU |
| Runtime | `/opt/zgs-ze-upm-preview/releases/<source-commit>`; atomic `current` symlink |
| State | `/var/lib/zgs-ze-upm-preview`; private storage and publisher authentication |
| Configuration | `/etc/zgs-ze-upm-preview`; secrets root-only and outside source control |

Install only into a verified absent target or use a reviewed update with a state
snapshot. Validate units before activation. Configure the dedicated Tunnel's
origin hostname to `http://127.0.0.1:19473`, with a catch-all 404. Install its token
and the origin key as root-only systemd credentials. Put the matching origin key
in the edge's `ORIGIN_KEY` secret binding. [`wrangler.toml`](wrangler.toml) declares
the edge and canonical domain; it contains no credentials. Preserve the current
secret binding during source updates. Publishing remains authenticated and
server-local, with immutable versioned tarballs.

For an upgrade, publish the candidate over loopback, upload identical immutable
GitHub release assets and metadata, then update the admission manifest and edge
together after verification. Never overwrite a published version. No background
GitHub puller or automatic consumer repin is installed by this Preview.

Verify normal TLS, anonymous origin denial, public write rejection, artifact
hashes, and a controlled origin outage/recovery through the same hostname. Use a
cold UPM cache to install, upgrade and roll back from project manifest/lock files;
verify `PackageSource.Registry`, source commit and actual API compilation. Keep
client profile files unchanged and retain receipts outside the repository.

Before retiring an earlier origin, snapshot its exact DNS/vhost/service state and
private package data, verify restore inputs, drain DNS caches, and prove the new
route with the old origin stopped. Rollback restores only owned records and
units from those snapshots. Preserve unrelated routes, certificates and secrets.
The earlier Shanghai bootstrap files (`preview_registry.py`, `nginx.conf`, and
the certificate hook) are retained as historical recovery inputs, not the active
deployment path.

## Consumer-locked Console trial

`prepare_pob_core.py` reads an existing consumer's manifest and lock, requires
matching Core/UI/Quantum/Agent Git pins, and packages Core from those committed
objects. It creates two distinct immutable trial versions with identical runtime
source and metadata GUIDs. Only package version and transport provenance change;
the consumer's files are read-only. The embedded Agent copy is instrumentation
for a disposable project and is never offered as a consumer installation route.

```text
python -B Tools/Packages/registry-preview/prepare_pob_core.py --project <consumer> --output <new-owned-output> --release-tag ze-upm-pob-core-<commit-prefix>-<YYYYMMDD>
python -B Tools/Packages/registry-preview/prepare_pob_core.py --project <consumer> --output <new-owned-output> --release-tag ze-upm-pob-console-<commit-prefix>-<YYYYMMDD> --with-core-version <verified-core-version>
```

The frontend archives pin their sibling package dependencies to the verified Core
version. That is the only dependency edit. The package catalogs record that graph
along with each Git tree and archive digest. Consumer adoption changes the three
manifest and lock entries together with the scoped registry. Refresh ZE Agent's
inventory from the installed `PackageInfo` after the project resolves and use its
installed Skill check. The Quantum SDK comes from the consumer project.
