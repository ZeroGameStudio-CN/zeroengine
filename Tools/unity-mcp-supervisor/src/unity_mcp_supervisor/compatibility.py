from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlparse

from .errors import IncompatibleError

PACKAGE_ID = "com.coplaydev.unity-mcp"
OFFICIAL_BASELINE_COMMIT = "c14de1e6dc01ab42d2bb358730cff954bce0ce6b"
INTERNAL_REVIEWED_COMMIT = "c2120b651176cdddfe80b3e5853b9c3738c1720e"
OFFICIAL_V1012_COMMIT = "4ce7dd3cc54e37e2ed6dc59cb5a047f3dccb3f50"
SUPPORTED_REFS = {
    "10.1.0",
    "v10.1.0",
    "10.1.2",
    "v10.1.2",
    OFFICIAL_BASELINE_COMMIT,
    INTERNAL_REVIEWED_COMMIT,
    OFFICIAL_V1012_COMMIT,
}


@dataclass(frozen=True)
class CompatibilityResult:
    status: str
    plugin_ref: str | None
    normalized_ref: str | None
    message: str

    @property
    def compatible(self) -> bool:
        return self.status in {"compatible", "approved"}


def read_plugin_reference(project_root: Path) -> str | None:
    for relative in ("Packages/packages-lock.json", "Packages/manifest.json"):
        path = project_root / relative
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (FileNotFoundError, OSError, ValueError, TypeError):
            continue
        if relative.endswith("packages-lock.json"):
            package = data.get("dependencies", {}).get(PACKAGE_ID)
            if isinstance(package, dict) and isinstance(package.get("version"), str):
                return package["version"]
        else:
            value = data.get("dependencies", {}).get(PACKAGE_ID)
            if isinstance(value, str):
                return value
    return None


def normalize_plugin_reference(reference: str | None) -> str | None:
    if not reference:
        return None
    value = reference.strip()
    if "#" in value:
        fragment = value.rsplit("#", 1)[1].strip()
        if fragment:
            return fragment
    parsed = urlparse(value)
    if parsed.scheme == "file":
        return value
    return value


def check_compatibility(
    project_root: Path, approved_refs: tuple[str, ...]
) -> CompatibilityResult:
    raw = read_plugin_reference(project_root)
    normalized = normalize_plugin_reference(raw)
    approved = {item.strip() for item in approved_refs}
    if raw in approved or normalized in approved:
        return CompatibilityResult(
            "approved", raw, normalized, "Plugin reference is approved locally."
        )
    if normalized in SUPPORTED_REFS:
        return CompatibilityResult(
            "compatible", raw, normalized, "Plugin reference is in the tested matrix."
        )
    version_match = re.fullmatch(r"v?(\d+)\.(\d+)\.(\d+)", normalized or "")
    if version_match and int(version_match.group(1)) != 10:
        return CompatibilityResult(
            "incompatible",
            raw,
            normalized,
            "Plugin major version is outside the v10 protocol baseline.",
        )
    return CompatibilityResult(
        "unknown",
        raw,
        normalized,
        "Plugin reference is not in the tested matrix; approve it only after a protocol smoke test.",
    )


def require_compatible(
    project_root: Path, approved_refs: tuple[str, ...]
) -> CompatibilityResult:
    result = check_compatibility(project_root, approved_refs)
    if not result.compatible:
        reference = result.normalized_ref or result.plugin_ref or "missing"
        raise IncompatibleError(
            f"Unity MCP plugin reference '{reference}' is {result.status}.",
            details={
                "compatibility": result.status,
                "plugin_ref": result.plugin_ref,
                "normalized_ref": result.normalized_ref,
                "hint": "Run a protocol smoke test, then use 'umcp config approve-plugin <ref>'.",
            },
        )
    return result
