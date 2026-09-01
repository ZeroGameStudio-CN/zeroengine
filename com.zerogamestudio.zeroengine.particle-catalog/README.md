# ZeroEngine Particle Catalog

Editor-only reusable core for particle asset catalogs.

It provides schema v2, bilingual taxonomy search, validated JSON persistence,
local Ollama visual classification, Windows credential storage, and a fixed
DeepSeek recommendation client. It does not scan project assets, depend on
Asset Inventory, or register project UI. Consumer projects provide those
adapters and own the shared catalog location.

The DeepSeek client uses `https://api.deepseek.com/chat/completions`, model
`deepseek-v4-flash`, non-thinking JSON output, at most 40 candidates, and at
most 1200 output tokens. API keys are never stored in project files.
