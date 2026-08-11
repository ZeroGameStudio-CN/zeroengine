from __future__ import annotations

import json
import socket
import threading
from collections.abc import Iterator
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def create_unity_project(root: Path, plugin_ref: str = "v10.1.2") -> Path:
    (root / "Assets").mkdir(parents=True)
    (root / "ProjectSettings").mkdir()
    (root / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 2022.3.62f3\n",
        encoding="utf-8",
    )
    (root / "Packages").mkdir()
    manifest = {
        "dependencies": {
            "com.coplaydev.unity-mcp": f"https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#{plugin_ref}"
        }
    }
    (root / "Packages" / "manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )
    lock = {
        "dependencies": {
            "com.coplaydev.unity-mcp": {
                "version": f"https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#{plugin_ref}",
                "depth": 0,
                "source": "git",
                "dependencies": {},
            }
        }
    }
    (root / "Packages" / "packages-lock.json").write_text(
        json.dumps(lock), encoding="utf-8"
    )
    return root


@contextmanager
def fake_http_server(
    instances: list[dict] | None = None,
    *,
    foreign: bool = False,
    drop_counter: list[int] | None = None,
    health_version: str = "10.1.2",
    receipt_protocol_unavailable: bool = False,
) -> Iterator[str]:
    instance_values = instances or []
    receipts: dict[str, dict] = {}

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args) -> None:
            return

        def _json(self, status: int, value: dict) -> None:
            body = json.dumps(value).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:
            parsed_path = urlsplit(self.path)
            if foreign:
                self._json(200, {"status": "something-else"})
                return
            if parsed_path.path == "/health":
                self._json(200, {"status": "healthy", "version": health_version})
                return
            if parsed_path.path == "/api/instances":
                public = [
                    {
                        key: value
                        for key, value in item.items()
                        if key not in {"project_root", "probe_error"}
                    }
                    for item in instance_values
                ]
                self._json(200, {"success": True, "instances": public})
                return
            prefix = "/api/command-receipts/"
            if parsed_path.path.startswith(prefix):
                command_id = parsed_path.path.removeprefix(prefix)
                receipt = receipts.get(command_id)
                if receipt is None:
                    self._json(
                        200,
                        {"success": True, "state": "missing", "command_id": command_id},
                    )
                else:
                    self._json(200, {"success": True, **receipt})
                return
            self._json(404, {"success": False})

        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(length) or b"{}")
            parsed_path = urlsplit(self.path)
            receipt_prefix = "/api/command-receipts/"
            if parsed_path.path.startswith(
                receipt_prefix
            ) and parsed_path.path.endswith("/ack"):
                command_id = parsed_path.path.removeprefix(receipt_prefix).removesuffix(
                    "/ack"
                )
                receipts.pop(command_id, None)
                self._json(200, {"success": True, "command_id": command_id})
                return
            project_hash = payload.get("unity_instance")
            instance = next(
                (item for item in instance_values if item.get("hash") == project_hash),
                None,
            )
            if instance is None:
                self._json(404, {"success": False, "error": "instance missing"})
                return
            if receipt_protocol_unavailable and payload.get("command_id"):
                self._json(409, {"code": "receipt_protocol_unavailable"})
                return
            if payload.get("type") == "drop_response":
                if drop_counter is not None:
                    drop_counter[0] += 1
                if payload.get("command_id"):
                    receipts[payload["command_id"]] = {
                        "state": "completed",
                        "command_id": payload["command_id"],
                        "result": {"success": True, "data": {"recovered": True}},
                    }
                try:
                    self.connection.shutdown(socket.SHUT_RDWR)
                except OSError:
                    pass
                self.connection.close()
                return
            if payload.get("type") == "ambiguous_response":
                if drop_counter is not None:
                    drop_counter[0] += 1
                if payload.get("command_id"):
                    receipts[payload["command_id"]] = {
                        "state": "ambiguous",
                        "command_id": payload["command_id"],
                    }
                try:
                    self.connection.shutdown(socket.SHUT_RDWR)
                except OSError:
                    pass
                self.connection.close()
                return
            if payload.get("type") == "server_error_after_receipt":
                if drop_counter is not None:
                    drop_counter[0] += 1
                if payload.get("command_id"):
                    receipts[payload["command_id"]] = {
                        "state": "completed",
                        "command_id": payload["command_id"],
                        "result": {"success": True, "data": {"recovered": True}},
                    }
                self._json(500, {"success": False, "error": "lost response"})
                return
            if payload.get("type") == "get_project_info":
                if instance.get("probe_error"):
                    self._json(200, {"success": False, "error": "probe unavailable"})
                    return
                self._json(
                    200,
                    {
                        "success": True,
                        "data": {
                            "projectRoot": instance["project_root"],
                            "projectName": instance.get("project", "Project"),
                        },
                    },
                )
                return
            self._json(200, {"success": True, "data": {"type": payload.get("type")}})

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{server.server_address[1]}"
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)
