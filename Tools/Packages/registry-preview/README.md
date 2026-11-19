# ZE Registry

ZE Registry is the shared ZeroEngine package feed at
`https://packages-preview.zerogamestudio.com`. The admission manifest lists the
43 admitted packages, including the three Console packages and the 40 packages
prepared from POB's tested `d194ba36` source. Packages use the same address for
installation, upgrade and rollback.

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
Discovery is generated from the admitted catalog without querying every
package upstream. Discovery responses report `X-ZE-Package-Source: catalog`.
Network errors, timeouts, unavailable responses and invalid artifacts select the
backup; origin authorization failures stay closed. Both invalid sources return
503. New versions become visible only after their backup and manifest are ready.

Anonymous GET/HEAD supports admitted packages, versioned tarballs, search and
ping. Public writes, account routes and unrelated packages are rejected. The
origin gate requires a private header supplied by the edge; client credentials
and cookies are never forwarded. Cloudflare remains a shared entry dependency;
GitHub fallback does not cover a Cloudflare outage or guarantee every network.

The project manifest sets the `ZE Registry` display name, lists the admitted
package scopes, and pins exact versions. The lock file records where Unity
resolves the packages. Teammates receive both files through SCM and download the
packages without personal npm/UPM/Git settings or GitHub access. The
[minimal project](sample/README.md) shows the Core-only trial.

POB first adopted signed version `0.1.0-ze-pob.d194ba36.4` for the three Console
packages in Plastic changeset `19381`. It then migrated Activity in `19413`,
10 independent packages in `19414`, and the 29 linked packages together in
`19415`. All 43 public ZE packages now resolve from ZE Registry. Each changeset
includes the matching package lock and generated ZE Agent inventory. The final
inventory covers 46 packages. The warm POB Editor verified the migrated
versions and signatures, retained the Quantum default entry, and passed both
command-catalog and parameter-metadata compatibility tests. The project owner
handles the Player build using the existing route.

The Activity delivery trial installed `1.1.0`, upgraded to
`1.1.1-ze-pilot.1` and rolled back in a disposable Unity project. The public
address returned the same signed archives from GitHub during a controlled
Worker01 origin outage. The other 39 packages passed a combined cold-cache
Unity installation from the public feed, with all 39 organization signatures
valid.

## Publish a version

Use the ZE main checkout and a tested full source commit. Set `ZE_RELEASE_EDITOR`
to the validated Unity Editor executable and `ZE_RELEASE_WRANGLER` to the
installed Wrangler JavaScript entry point. The controller also uses the existing
`unity`, `node`, `ssh zgs-worker-linux-01`, `gh` and Wrangler operator sessions.
The signing credentials stay with the installed Worker01 publisher.

```text
python -B Tools/Packages/registry-preview/release.py --commit <tested-commit> --package <changed-package-id> --output <new-external-directory>
```

Repeat `--package` for a related cohort. The command reuses admitted dependencies
when their pins, source tree and signer agree. A changed package needs a new
version. `NAME=VERSION` selects an explicit version override for a reviewed trial.
The command signs on Worker01, verifies the exact archives in one disposable
Unity run, publishes the same bytes to Worker01 and GitHub, and verifies the
active HTTPS catalog and archives. For an Agent release, the Unity run also
captures the installed package snapshot for a Skill apply and drift check.

The output includes `timings.jsonl`, the Unity and deployment receipts, and the
candidate catalog. A verified promotion updates the checked-out admission
catalog. Commit that catalog on ZE main before the next release. A consumer
adopts an updated version through its own manifest, lock and Skill submission.

For a known partial release, run the same command with `--resume` and the same
commit, package arguments and output. This reuses the verified signed bytes and
receipts. An unresolved Unity attempt or Cloudflare activation needs its original
readback before another attempt. The single publication lock covers cooperating
controllers on Worker01.

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

## Manual release and recovery steps

[`sign_release.py`](sign_release.py) prepares selected public ZE packages and
their dependency closure from one tested Git commit already reachable on public
`main`. It keeps the source package version unless the release explicitly
selects another. Existing versions can be reused when their tree, dependency
pins and signer match. Source files and GUIDs are preserved.

```text
python -B Tools/Packages/registry-preview/sign_release.py --prepare-from-commit <tested-40-character-commit> --package <package-name> --against-catalog Tools/Packages/registry-preview/release-manifest.mjs --organization-id <Unity-organization-id> --output <new-prepared-directory>
```

Transfer the prepared directory to the publisher. The Worker01 signing tool
reads `/etc/zgs-ze-upm-preview/signing.env`, a root-owned `0600` file
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

Repeat `--package` to select a cohort. Use `--package <name>=<version>` for a
reviewed version override. Keep signing output outside the source directories.
The signed report identifies the exact archive hashes. Use Unity in a
disposable project to confirm `PackageInfo.signature` is `Valid` for each
archive. Use [`verify_release.py`](verify_release.py) with an approved disposable
output directory and the installed Unity CLI and Editor. It verifies the actual
package signature after Unity compiles the selected cohort and writes the
`unity-trust-receipt.json` needed for promotion. Known UPM rewrites of manifest
whitespace and omission of Git control files are reported by the archive
verifier. Runtime files and GUIDs still match the signed source bytes exactly.

```text
python -B verify_release.py --prepared <prepared-directory> --signed <signed-directory> --output <new-probe-directory> --unity <unity-cli> --editor <editor-executable>
```

The Worker01 publisher stages those exact archives over loopback and exports
metadata and hashes. The maintainer side creates the immutable GitHub backup,
checks each remote asset digest, then writes the candidate admission catalog.
It can add a new version to a known package without uploading the old
tarballs again. Older versions retain their original backup locations.

```text
sudo python3 -B publish_release.py origin --cohort <prepared-directory> <signed-directory> --output <new-export-directory>
python -B publish_release.py backup --catalog Tools/Packages/registry-preview/release-manifest.mjs --export <worker-export-directory> --unity-trust-receipt <unity-receipt.json> --tag <new-ze-upm-release-tag> --output <new-promotion-directory>
```

Run the backup step with the existing maintainer GitHub CLI authorization. Use
[`activate_release.py`](activate_release.py) with the current Worker version ID
from `wrangler deployments status`. It uploads a version with the existing
bindings, admits the verified catalog and checks the new archive URLs. Provide
`--verify-unity` to include a fresh Unity installation and signature check in
the same rollback guard. It restores the previous Worker version if either
check fails. A failed signature, conflicting version or backup upload during
preparation leaves the previous admission active.

```text
python -B activate_release.py --promotion <promotion-directory> --catalog release-manifest.mjs --node <node-executable> --wrangler <wrangler-cli-script> --expect-version <current-Worker-version-id> --verify-unity <prepared-directory> <signed-directory> <unity-cli> <editor-executable>
```

Save the newly verified catalog in source control after activation.
`verify_release.py --public` can also recheck an active cohort independently.
Migrate a consumer's Plastic manifest and lock together after the required
install, upgrade and rollback checks.

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
