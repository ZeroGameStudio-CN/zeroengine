# ZeroEngine Asset Catalog

Editor-only reusable contracts for a reviewable, shared Unity asset catalog.
It contains stable asset identity, taxonomy/proposal validation, local snapshot
search, Catalog API transport, classification exchange validation, Windows
Credential Manager/macOS Keychain secret storage, and personal OpenAI-compatible
AI recommendations.

The package intentionally does not reference POB, Asset Inventory, project
folders, a shared SQLite file, or an AI gateway. A consuming project provides
its adapter, preview provider, project ID, endpoint, and UI.

Production consumers must reference a tested Git commit and keep all
ZeroEngine package pins aligned. `file:` package references are only for
temporary local debugging and must not be handed off.

Personal AI calls are explicit user actions. The client sends at most 40
already-retrieved candidates, rejects absolute paths and unlisted identities,
and never writes an answer back to the shared catalog.
