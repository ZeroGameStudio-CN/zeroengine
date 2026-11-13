"""Supported Editor baseline shared by package validation and test-project creation."""
import re


MINIMUM_UNITY = (6000, 6, 0)
EDITOR_VERSION = "6000.6.4f1"
TEST_FRAMEWORK_VERSION = "1.8.0"


def unity_version(value):
    match = re.match(r"^(\d+)\.(\d+)(?:\.(\d+))?", value)
    if not match:
        raise ValueError("Invalid Unity version: " + value)
    return tuple(int(part or 0) for part in match.groups())


def require_supported_editor(value):
    if unity_version(value) < MINIMUM_UNITY:
        raise ValueError("ZeroEngine requires Unity 6000.6 or newer: " + value)
