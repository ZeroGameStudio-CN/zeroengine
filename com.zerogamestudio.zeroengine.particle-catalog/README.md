# ZeroEngine Particle Catalog

Editor-only reusable core for particle asset catalogs.

It provides schema v2, bilingual taxonomy search, validated JSON persistence,
local Ollama visual classification, and a fixed DeepSeek recommendation client.
Consumer projects provide asset scanning, UI adapters and the catalog location.

Particle Catalog 0.1.2 uses Asset Catalog 0.1.2 for its Editor chat transport
and Windows credential store. For Git UPM, pin both packages to the same tested
ZE commit. The particle API key still uses
`ZeroEngine.ParticleCatalog.DeepSeekApiKey` with UTF-16 bytes on Windows and
session storage on other platforms.

The DeepSeek client uses `https://api.deepseek.com/chat/completions`, model
`deepseek-v4-flash`, non-thinking JSON output, at most 40 candidates, and at
most 1200 output tokens. API keys are never stored in project files.
