from __future__ import annotations

import os
import shlex
import subprocess
import sys
from pathlib import Path

from .errors import ServiceError
from .service_state import Settings, _atomic_write, _unlink_with_retry

MARKER = "unity-mcp-supervisor managed startup"


def _ensure_command(settings: Settings) -> list[str]:
    return [
        sys.executable,
        "-m",
        "unity_mcp_supervisor.cli",
        "--state-dir",
        str(settings.state_dir),
        "service",
        "ensure",
    ]


def startup_file(settings: Settings) -> Path:
    if sys.platform == "win32":
        app_data = os.environ.get("APPDATA")
        if not app_data:
            raise ServiceError("APPDATA is unavailable; cannot install user startup.")
        return (
            Path(app_data)
            / "Microsoft"
            / "Windows"
            / "Start Menu"
            / "Programs"
            / "Startup"
            / "unity-mcp-supervisor.vbs"
        )
    if sys.platform == "darwin":
        return (
            Path.home()
            / "Library"
            / "LaunchAgents"
            / "com.zerogamestudio.unity-mcp-supervisor.plist"
        )
    return Path.home() / ".config" / "autostart" / "unity-mcp-supervisor.desktop"


def enable_startup(settings: Settings) -> Path:
    path = startup_file(settings)
    if path.exists():
        try:
            existing = path.read_text(encoding="utf-8")
        except OSError as exc:
            raise ServiceError(f"Cannot inspect startup file {path}: {exc}") from exc
        if MARKER not in existing:
            raise ServiceError(
                f"Refusing to overwrite unrecognized startup file: {path}"
            )
    command = _ensure_command(settings)
    if sys.platform == "win32":
        command_line = subprocess.list2cmdline(command).replace('"', '""')
        content = (
            f"' {MARKER}\r\n"
            'Set shell = CreateObject("WScript.Shell")\r\n'
            f'shell.Run "{command_line}", 0, False\r\n'
        )
    elif sys.platform == "darwin":
        arguments = "".join(f"<string>{_xml_escape(item)}</string>" for item in command)
        content = (
            '<?xml version="1.0" encoding="UTF-8"?>\n'
            '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" '
            '"http://www.apple.com/DTDs/PropertyList-1.0.dtd">\n'
            '<plist version="1.0"><dict>\n'
            f"<key>Label</key><string>com.zerogamestudio.unity-mcp-supervisor</string><!-- {MARKER} -->\n"
            f"<key>ProgramArguments</key><array>{arguments}</array>\n"
            "<key>RunAtLoad</key><true/>\n"
            "</dict></plist>\n"
        )
    else:
        content = (
            "[Desktop Entry]\n"
            "Type=Application\n"
            "Name=Unity MCP Supervisor\n"
            f"Comment={MARKER}\n"
            f"Exec={shlex.join(command)}\n"
            "X-GNOME-Autostart-enabled=true\n"
        )
    _atomic_write(path, content)
    return path


def disable_startup(settings: Settings) -> Path:
    path = startup_file(settings)
    if not path.exists():
        return path
    try:
        content = path.read_text(encoding="utf-8")
    except OSError as exc:
        raise ServiceError(f"Cannot inspect startup file {path}: {exc}") from exc
    if MARKER not in content:
        raise ServiceError(f"Refusing to remove unrecognized startup file: {path}")
    _unlink_with_retry(path)
    return path


def _xml_escape(value: str) -> str:
    return (
        value.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
        .replace("'", "&apos;")
    )
