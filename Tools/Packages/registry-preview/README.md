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

POB adopted signed version `0.1.0-ze-pob.d194ba36.4` for all three packages in
Plastic changeset `19381`. The current Editor reports Registry provenance and
three valid organization signatures, retains the Quantum default input entry,
and passes the parameter-metadata and command-catalog checks. Its installed ZE
Agent Skill check covers 46 packages with no drift; only the three Console
inventory entries changed. Player builds remain with the project owner.

## Package signatures

Versions `0.1.0-ze-pob.d194ba36.3` and `.4` contain Unity organization signatures
for ZeroGameStudio. Unity 6000.6.9f1 reports `Valid` for all three packages during
cold-cache registry installation, signed upgrade and rollback. The archives keep
the frozen runtime and GUID bytes; each adds one `.attestation.p7m` file.

The earlier `.2` cohort remains unsigned for historical rollback. New upgrades
must use freshly signed immutable versions. No signature validation or package
security preference is disabled. Unity may show a publisher-origin indicator to
accounts outside the signing organization; the archives still have valid signatures.

Package signature handling follows the [Unity signing documentation](https://docs.unity.com/en-us/engine/6000.6/manual/packages-list/upm-signature-lp/upm-sign-methods).

[`sign_release.py`](sign_release.py) prepares the three package directories from
one tested Git commit, updates only version/provenance and sibling pins, then
uses the official UPM CLI to sign them. Preparation needs no Unity credentials:

```text
python -B Tools/Packages/registry-preview/sign_release.py --prepare-from-commit <tested-40-character-commit> --version <new-immutable-version> --organization-id <Unity-organization-id> --output <new-prepared-directory>
```

Transfer the prepared directory and the two maintained Python files
(`sign_release.py`, `prepare_pob_core.py`) to the publisher. The Worker01 signing
operator reads `/etc/zgs-ze-upm-preview/signing.env`, a root-owned `0600` file
containing `UPM_SERVICE_ACCOUNT_KEY_ID` and `UPM_SERVICE_ACCOUNT_KEY_SECRET`.
The dedicated `ze-upm-package-signer` account has only the organization-level
**Package Manager Package Signer** role. Credentials never enter archives,
command arguments, logs or consumer configuration.

Worker01's installed tool is
`/opt/zgs-ze-upm-preview/signing/current/sign_release.py`, with the pinned
official CLI at `/opt/zgs-ze-upm-preview/tools/upm/9.36.0/UnityPackageManager`.
Ordinary package upgrades reuse that installation; changed operator code is
deployed from a new committed tool release.

```text
sudo python3 -B sign_release.py --input <prepared-directory>/release-input.json --output <new-signed-directory> --upm <official-UPM-executable> --credentials /etc/zgs-ze-upm-preview/signing.env
python -B sign_release.py --input <prepared-directory>/release-input.json --output <signed-directory> --verify-only
```

Keep signing output outside the source directories. Existing output is preserved
instead of silently repacked. Byte verification checks the exact source and GUID
files plus the added attestation; Unity's actual `PackageInfo.signature` verdict
is a separate required check before consumer adoption. Publish the exact signed
bytes and immutable backup before admitting the new version at the edge.

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
The three services use startup ordering and `Wants` dependencies. A stopped
registry or origin gate must leave its downstream gate/Tunnel running so that
recovery returns traffic to Worker01 without restarting dependent services.
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
