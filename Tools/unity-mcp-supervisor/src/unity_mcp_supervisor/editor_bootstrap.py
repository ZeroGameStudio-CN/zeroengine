from __future__ import annotations

import ctypes
import json
import os
import subprocess
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

from .errors import (
    EditorControlUnsupportedError,
    EditorNotOpenError,
    EditorRestartRequiredError,
    ProjectError,
)
from .project_resolver import ProjectResolver, ResolvedProject
from .rest_client import RestClient
from .service_state import Settings, process_alive

_WINDOWS_PREFS_KEY = r"Software\Unity Technologies\Unity Editor 5.x"
_WINDOWS_PREF_VALUES = {
    "use_http": "MCPForUnity.UseHttpTransport_h3850471145",
    "scope": "MCPForUnity.HttpTransportScope_h2378119776",
    "endpoint": "MCPForUnity.HttpUrl_h1802602754",
    "auto_start": "MCPForUnity.AutoStartOnLoad_h2539145689",
}
_WINDOWS_SERVER_OWNERSHIP_PREF_VALUES = (
    "MCPForUnity.LocalHttpServer.LastPidFilePath_h2108398323",
    "MCPForUnity.LocalHttpServer.LastInstanceToken_h3764283543",
)


@dataclass(frozen=True)
class EditorInstance:
    pid: int
    unity_version: str
    app_path: str


@dataclass(frozen=True)
class BootstrapEvidence:
    mode: str
    editor_pid: int
    previous_editor_pid: int | None
    unity_version: str
    prefs_configured: bool
    restarted: bool
    control_channel: str | None = None
    companion_version: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def _canonical_path(value: str | Path) -> str:
    return os.path.normcase(os.path.realpath(Path(value)))


def _process_executable(pid: int) -> str | None:
    if sys.platform == "win32":
        from ctypes import wintypes

        process_query_limited_information = 0x1000
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        open_process = kernel32.OpenProcess
        open_process.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        open_process.restype = wintypes.HANDLE
        query_path = kernel32.QueryFullProcessImageNameW
        query_path.argtypes = [
            wintypes.HANDLE,
            wintypes.DWORD,
            wintypes.LPWSTR,
            ctypes.POINTER(wintypes.DWORD),
        ]
        query_path.restype = wintypes.BOOL
        close_handle = kernel32.CloseHandle
        close_handle.argtypes = [wintypes.HANDLE]
        close_handle.restype = wintypes.BOOL
        handle = open_process(process_query_limited_information, False, pid)
        if not handle:
            return None
        try:
            buffer = ctypes.create_unicode_buffer(32768)
            size = wintypes.DWORD(len(buffer))
            if not query_path(handle, 0, buffer, ctypes.byref(size)):
                return None
            return buffer.value
        finally:
            close_handle(handle)

    if sys.platform == "darwin":
        try:
            libproc = ctypes.CDLL("/usr/lib/libproc.dylib")
            buffer = ctypes.create_string_buffer(4096)
            if libproc.proc_pidpath(pid, buffer, len(buffer)) <= 0:
                return None
            return os.fsdecode(buffer.value)
        except (OSError, AttributeError):
            return None

    try:
        return os.readlink(f"/proc/{pid}/exe")
    except OSError:
        return None


def _read_editor_record(project_root: Path) -> EditorInstance:
    instance_path = project_root / "Library" / "EditorInstance.json"
    try:
        value = json.loads(instance_path.read_text(encoding="utf-8-sig"))
        return EditorInstance(
            pid=int(value["process_id"]),
            unity_version=str(value["version"]),
            app_path=str(value["app_path"]),
        )
    except (FileNotFoundError, OSError, ValueError, TypeError, KeyError) as exc:
        raise EditorNotOpenError(
            "The project has no valid EditorInstance.json launch record.",
            details={
                "hint": "Open this Unity project once, then rerun the same umcp command."
            },
        ) from exc


def read_editor_instance(project_root: Path) -> EditorInstance:
    record = _read_editor_record(project_root)
    executable = _process_executable(record.pid) if process_alive(record.pid) else None
    if not executable or _canonical_path(executable) != _canonical_path(
        record.app_path
    ):
        raise EditorNotOpenError(
            "The target Unity Editor instance is not currently open and verified.",
            details={"editor_pid": record.pid},
        )
    return record


def _binary_pref(value: str) -> bytes:
    return value.encode("utf-8") + b"\0"


def configure_editor_prefs(endpoint: str) -> None:
    if sys.platform != "win32":
        raise EditorControlUnsupportedError(
            "Pure CLI Editor configuration is not verified on this platform."
        )
    import winreg

    try:
        with winreg.CreateKeyEx(
            winreg.HKEY_CURRENT_USER,
            _WINDOWS_PREFS_KEY,
            0,
            winreg.KEY_SET_VALUE,
        ) as key:
            winreg.SetValueEx(
                key, _WINDOWS_PREF_VALUES["use_http"], 0, winreg.REG_DWORD, 1
            )
            winreg.SetValueEx(
                key,
                _WINDOWS_PREF_VALUES["scope"],
                0,
                winreg.REG_BINARY,
                _binary_pref("local"),
            )
            winreg.SetValueEx(
                key,
                _WINDOWS_PREF_VALUES["endpoint"],
                0,
                winreg.REG_BINARY,
                _binary_pref(endpoint),
            )
            winreg.SetValueEx(
                key, _WINDOWS_PREF_VALUES["auto_start"], 0, winreg.REG_DWORD, 1
            )
    except OSError as exc:
        raise EditorControlUnsupportedError(
            "Cannot write the current-user Unity EditorPrefs values."
        ) from exc


def clear_editor_server_ownership() -> bool:
    """Clear stale Unity-owned server handshakes while the supervisor owns it."""
    if sys.platform != "win32":
        return False
    import winreg

    changed = False
    try:
        with winreg.CreateKeyEx(
            winreg.HKEY_CURRENT_USER,
            _WINDOWS_PREFS_KEY,
            0,
            winreg.KEY_SET_VALUE,
        ) as key:
            for value_name in _WINDOWS_SERVER_OWNERSHIP_PREF_VALUES:
                try:
                    winreg.DeleteValue(key, value_name)
                    changed = True
                except FileNotFoundError:
                    pass
    except OSError as exc:
        raise EditorControlUnsupportedError(
            "Cannot clear stale Unity MCP server ownership state."
        ) from exc
    return changed


def _read_windows_prefs() -> dict[str, Any] | None:
    if sys.platform != "win32":
        return None
    import winreg

    try:
        with winreg.OpenKey(
            winreg.HKEY_CURRENT_USER, _WINDOWS_PREFS_KEY, 0, winreg.KEY_READ
        ) as key:
            raw_scope = winreg.QueryValueEx(key, _WINDOWS_PREF_VALUES["scope"])[0]
            raw_endpoint = winreg.QueryValueEx(key, _WINDOWS_PREF_VALUES["endpoint"])[0]
            return {
                "use_http": bool(
                    winreg.QueryValueEx(key, _WINDOWS_PREF_VALUES["use_http"])[0]
                ),
                "scope": bytes(raw_scope).rstrip(b"\0").decode("utf-8"),
                "endpoint": bytes(raw_endpoint).rstrip(b"\0").decode("utf-8"),
                "auto_start": bool(
                    winreg.QueryValueEx(key, _WINDOWS_PREF_VALUES["auto_start"])[0]
                ),
            }
    except (OSError, UnicodeDecodeError, TypeError):
        return None


def _prefs_configured(endpoint: str) -> bool:
    prefs = _read_windows_prefs()
    return bool(
        prefs
        and prefs["use_http"]
        and prefs["scope"] == "local"
        and prefs["endpoint"] == endpoint
        and prefs["auto_start"]
    )


def enforce_editor_prefs(endpoint: str) -> bool:
    if sys.platform != "win32" or _prefs_configured(endpoint):
        return False
    configure_editor_prefs(endpoint)
    return True


def _visible_top_level_windows(pid: int) -> list[tuple[int, str]]:
    if sys.platform != "win32":
        raise EditorControlUnsupportedError(
            "Unity Editor window inspection is not verified on this platform."
        )
    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    enum_windows = user32.EnumWindows
    get_pid = user32.GetWindowThreadProcessId
    is_visible = user32.IsWindowVisible
    get_owner = user32.GetWindow
    get_text_length = user32.GetWindowTextLengthW
    get_text = user32.GetWindowTextW
    enum_windows.argtypes = [callback_type, wintypes.LPARAM]
    enum_windows.restype = wintypes.BOOL
    get_pid.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    get_pid.restype = wintypes.DWORD
    is_visible.argtypes = [wintypes.HWND]
    is_visible.restype = wintypes.BOOL
    get_owner.argtypes = [wintypes.HWND, wintypes.UINT]
    get_owner.restype = wintypes.HWND
    get_text_length.argtypes = [wintypes.HWND]
    get_text_length.restype = ctypes.c_int
    get_text.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    get_text.restype = ctypes.c_int
    gw_owner = 4
    windows: list[tuple[int, str]] = []

    @callback_type
    def callback(hwnd, _lparam):
        window_pid = wintypes.DWORD()
        get_pid(hwnd, ctypes.byref(window_pid))
        title_length = get_text_length(hwnd)
        if (
            window_pid.value == pid
            and is_visible(hwnd)
            and not get_owner(hwnd, gw_owner)
            and title_length > 0
        ):
            title_buffer = ctypes.create_unicode_buffer(title_length + 1)
            get_text(hwnd, title_buffer, len(title_buffer))
            title = title_buffer.value
            handle = int(hwnd) if isinstance(hwnd, int) else int(hwnd.value)
            windows.append((handle, title))
        return True

    if not enum_windows(callback, 0):
        raise EditorControlUnsupportedError("Cannot enumerate Unity Editor windows.")
    return windows


def _request_windows_close(pid: int, project_name: str) -> None:
    if sys.platform != "win32":
        raise EditorControlUnsupportedError(
            "Safe Unity Editor restart is not verified on this platform."
        )
    from ctypes import wintypes

    expected_prefix = f"{project_name.casefold()} -"
    candidates = [
        (handle, title)
        for handle, title in _visible_top_level_windows(pid)
        if title.casefold().startswith(expected_prefix)
    ]
    if len(candidates) != 1:
        raise EditorRestartRequiredError(
            "Cannot uniquely identify the target Unity Editor main window.",
            details={
                "editor_pid": pid,
                "project_name": project_name,
                "matching_window_titles": [title for _, title in candidates],
            },
        )
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    post_message = user32.PostMessageW
    post_message.argtypes = [
        wintypes.HWND,
        wintypes.UINT,
        wintypes.WPARAM,
        wintypes.LPARAM,
    ]
    post_message.restype = wintypes.BOOL
    wm_close = 0x0010
    if not post_message(candidates[0][0], wm_close, 0, 0):
        raise EditorControlUnsupportedError(
            "Cannot request a normal close from the target Unity Editor.",
            details={"editor_pid": pid},
        )


def _launch_editor(project_root: Path, record: EditorInstance) -> subprocess.Popen:
    executable = Path(record.app_path)
    if not executable.is_file():
        raise EditorControlUnsupportedError(
            "The recorded Unity Editor executable no longer exists.",
            details={"unity_version": record.unity_version},
        )
    kwargs: dict[str, Any] = {
        "cwd": str(project_root),
        "stdin": subprocess.DEVNULL,
        "stdout": subprocess.DEVNULL,
        "stderr": subprocess.DEVNULL,
        "close_fds": True,
    }
    if os.name == "nt":
        kwargs["creationflags"] = (
            subprocess.CREATE_NEW_PROCESS_GROUP
            | subprocess.DETACHED_PROCESS
            | subprocess.CREATE_NO_WINDOW
            | subprocess.CREATE_BREAKAWAY_FROM_JOB
        )
    else:
        kwargs["start_new_session"] = True
    try:
        return subprocess.Popen(
            [str(executable), "-projectPath", str(project_root)], **kwargs
        )
    except OSError as exc:
        raise EditorControlUnsupportedError(
            "Cannot relaunch the Unity Editor."
        ) from exc


def _wait_for_exit(pid: int, deadline: float) -> None:
    while process_alive(pid):
        if time.monotonic() >= deadline:
            raise EditorRestartRequiredError(
                "Unity did not close normally; unsaved content or a modal may be blocking exit.",
                details={
                    "editor_pid": pid,
                    "hint": "Resolve the Unity prompt, then rerun connect --restart-editor.",
                },
            )
        time.sleep(0.25)


def _wait_for_new_editor(
    project_root: Path,
    previous_pid: int,
    launched: subprocess.Popen,
    deadline: float,
) -> EditorInstance:
    while time.monotonic() < deadline:
        if launched.poll() is not None:
            raise EditorControlUnsupportedError(
                "The relaunched Unity process exited before opening the project."
            )
        try:
            current = read_editor_instance(project_root)
        except EditorNotOpenError:
            current = None
        if current is not None and current.pid != previous_pid:
            return current
        if sys.platform == "win32":
            windows = _visible_top_level_windows(launched.pid)
            safe_mode_titles = [
                title for _, title in windows if "safe mode" in title.casefold()
            ]
            if safe_mode_titles:
                raise EditorRestartRequiredError(
                    "The relaunched Unity Editor is waiting at the Safe Mode prompt because the project has compilation errors.",
                    details={
                        "editor_pid": launched.pid,
                        "window_titles": safe_mode_titles,
                        "hint": "Resolve the compile errors or choose Safe Mode in Unity; umcp never dismisses this dialog.",
                    },
                )
        time.sleep(0.5)
    raise EditorRestartRequiredError(
        "Timed out waiting for the relaunched Unity Editor instance.",
        details={"previous_editor_pid": previous_pid},
    )


def restart_editor(
    project_root: Path, settings: Settings, deadline: float
) -> EditorInstance:
    record = _read_editor_record(project_root)
    executable = _process_executable(record.pid) if process_alive(record.pid) else None
    if executable is None:
        raise EditorNotOpenError(
            "The target Unity Editor instance is not currently open and verified.",
            details={"editor_pid": record.pid},
        )
    if _canonical_path(executable) != _canonical_path(record.app_path):
        raise EditorNotOpenError(
            "EditorInstance.json PID belongs to a different executable.",
            details={"editor_pid": record.pid},
        )
    _request_windows_close(record.pid, project_root.name)
    close_deadline = min(deadline, time.monotonic() + 60.0)
    _wait_for_exit(record.pid, close_deadline)
    configure_editor_prefs(settings.endpoint)
    launched = _launch_editor(project_root, record)
    return _wait_for_new_editor(project_root, record.pid, launched, deadline)


def ensure_project_connection(
    project_root: Path,
    settings: Settings,
    client: RestClient,
    timeout_seconds: float | None = None,
    *,
    allow_editor_restart: bool = False,
) -> tuple[ResolvedProject, BootstrapEvidence]:
    resolver = ProjectResolver(client)
    resolved: ResolvedProject | None = None
    try:
        resolved = resolver.resolve_once(project_root)
    except ProjectError as exc:
        if exc.details.get("matching_hashes"):
            raise
    if resolved is not None and not allow_editor_restart:
        return resolved, BootstrapEvidence(
            mode="existing-session",
            editor_pid=0,
            previous_editor_pid=None,
            unity_version=resolved.unity_version,
            prefs_configured=False,
            restarted=False,
        )
    budget = (
        timeout_seconds
        if timeout_seconds is not None
        else settings.bootstrap_timeout_seconds
    )
    if budget <= 0:
        raise EditorRestartRequiredError(
            "Connection timeout must be greater than zero."
        )
    if not allow_editor_restart:
        editor = read_editor_instance(project_root)
        from .editor_control import request_editor_connect

        started = time.monotonic()
        control = request_editor_connect(
            project_root,
            settings,
            editor.pid,
            budget,
        )
        remaining = budget - (time.monotonic() - started)
        if remaining <= 0:
            raise EditorRestartRequiredError(
                "Connection budget expired after the companion Connect request."
            )
        resolved = resolver.wait(project_root, remaining)
        return resolved, BootstrapEvidence(
            mode="companion-hot-connect",
            editor_pid=editor.pid,
            previous_editor_pid=None,
            unity_version=resolved.unity_version,
            prefs_configured=True,
            restarted=False,
            control_channel=control.channel,
            companion_version=control.companion_version,
        )
    deadline = time.monotonic() + budget
    previous = _read_editor_record(project_root)
    current = restart_editor(project_root, settings, deadline)
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise EditorRestartRequiredError(
            "Connection budget expired after relaunching Unity."
        )
    resolved = resolver.wait(project_root, remaining)
    return resolved, BootstrapEvidence(
        mode="configured-editor-restart",
        editor_pid=current.pid,
        previous_editor_pid=previous.pid,
        unity_version=current.unity_version,
        prefs_configured=True,
        restarted=True,
    )


def bootstrap_diagnostics(
    project_root: Path, endpoint: str, state_dir: Path | None = None
) -> dict[str, Any]:
    editor: EditorInstance | None = None
    try:
        editor = read_editor_instance(project_root)
    except EditorNotOpenError:
        pass
    result = {
        "editor_open": editor is not None,
        "editor_pid": editor.pid if editor else None,
        "unity_version": editor.unity_version if editor else None,
        "control_supported": sys.platform == "win32",
        "prefs_configured": _prefs_configured(endpoint),
    }
    from .editor_control import editor_control_diagnostics

    settings = Settings.load(state_dir, endpoint)
    result["hot_connect"] = editor_control_diagnostics(
        project_root, settings, editor.pid if editor else None
    )
    return result
