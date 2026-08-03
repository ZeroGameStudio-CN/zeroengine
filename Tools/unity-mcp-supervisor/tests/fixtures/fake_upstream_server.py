from __future__ import annotations

import argparse
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--http-host", default="127.0.0.1")
    parser.add_argument("--http-port", type=int, required=True)
    parser.add_argument("--pidfile")
    args, _ = parser.parse_known_args()
    if args.pidfile:
        Path(args.pidfile).write_text(f"{os.getpid()}\n", encoding="ascii")
    count_file = os.environ.get("UMCP_TEST_START_COUNT_FILE")
    if count_file:
        with Path(count_file).open("a", encoding="ascii") as stream:
            stream.write(f"{os.getpid()}\n")

    instances = json.loads(os.environ.get("UMCP_TEST_INSTANCES_JSON", "[]"))
    health_fail_file = os.environ.get("UMCP_TEST_HEALTH_FAIL_FILE")

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args) -> None:
            return

        def send_json(self, status: int, value: dict) -> None:
            body = json.dumps(value).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:
            if self.path == "/health":
                if health_fail_file and Path(health_fail_file).exists():
                    self.send_json(503, {"status": "unhealthy"})
                else:
                    self.send_json(200, {"status": "healthy", "version": "10.1.2"})
            elif self.path == "/api/instances":
                public = [
                    {key: value for key, value in item.items() if key != "project_root"}
                    for item in instances
                ]
                self.send_json(200, {"success": True, "instances": public})
            else:
                self.send_json(404, {"success": False})

        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(length) or b"{}")
            project_hash = payload.get("unity_instance")
            instance = next(
                (item for item in instances if item.get("hash") == project_hash), None
            )
            if instance is None:
                self.send_json(404, {"success": False, "error": "instance missing"})
            elif payload.get("type") == "get_project_info":
                self.send_json(
                    200,
                    {
                        "success": True,
                        "data": {"projectRoot": instance["project_root"]},
                    },
                )
            else:
                self.send_json(
                    200, {"success": True, "data": {"type": payload.get("type")}}
                )

    server = ThreadingHTTPServer((args.http_host, args.http_port), Handler)
    server.serve_forever()


if __name__ == "__main__":
    main()
