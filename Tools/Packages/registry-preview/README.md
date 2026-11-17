# Single-package UPM HTTPS Preview

An isolated distribution trial for `com.zerogamestudio.zeroengine.console`.
It does not repin consumer projects or establish a production package service.

- Public origin: `https://packages-preview.zerogamestudio.com`.
- Public access: anonymous GET/HEAD for this package, npm search and ping only.
  Publishing, account endpoints and unrelated packages are not publicly routed.
- Runtime: Verdaccio 7.1.1 with Node 24.11.1, pinned npm lock, no uplinks or web UI.
- Publication: server-local authenticated maintainer; immutable versioned tarballs.
- State: `/var/lib/zgs-ze-upm-preview`; runtime releases:
  `/opt/zgs-ze-upm-preview/releases/<source-commit>`. Credentials remain server-local.
- Unit: `zgs-ze-upm-preview.service`, loopback `19196`, DynamicUser,
  256 MiB memory and 50% CPU limits. It needs no Worker or GitHub for reads.

The project manifest carries the registry URL and the exact version; the lock
file carries its resolved registry provenance. Teammates receive those files
through their existing SCM. Their personal npm/UPM/Git configuration is not an
installation prerequisite for this anonymous read-only feed.

Stage the source files, verified Node archive and the two trial tarballs in an
owned maintenance directory. `operator.py dns` snapshots/creates only this DNS
record using the existing server-local Cloudflare credential. `operator.py install`
creates a previously absent runtime/unit; it does not replace an existing service.
Issue the dedicated DNS-01 certificate with the existing Certbot account, then
`operator.py edge` validates and activates only the new Nginx vhost. Publish the
two versions over loopback before user-facing verification. Existing Node,
website, Analytics and Ops services remain separate.

Verification must include normal certificate validation, direct public DNS,
metadata/tarball hashes, public write rejection, cold-cache install/upgrade/
rollback through the Unity Client API and a second fresh project opening from
only the checked-in project configuration. A local-only test is not public-network
evidence. Agent Registry provenance and whole-cohort migration remain separate work.

Rollback stops/disables only this unit and removes only its unchanged vhost and
owned DNS record, with `nginx -t` before reload. Preserve package state and the
deployment receipt. Do not remove unrelated certificates, credentials or routes.
