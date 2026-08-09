# Adapter Template

Project adapters can expose custom visual sources by implementing `ITcePresentationSource`.

Keep gameplay semantics in the project adapter. The presentation package should receive a visual snapshot request, capture renderer state, and play visuals only.
