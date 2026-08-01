from __future__ import annotations

import json
import os
import sys
from pathlib import Path

output = os.environ.get("UMCP_TEST_CLI_CAPTURE")
payload = {
    "args": sys.argv[1:],
    "host": os.environ.get("UNITY_MCP_HOST"),
    "port": os.environ.get("UNITY_MCP_HTTP_PORT"),
    "instance": os.environ.get("UNITY_MCP_INSTANCE"),
    "format": os.environ.get("UNITY_MCP_FORMAT"),
}
if output:
    Path(output).write_text(json.dumps(payload), encoding="utf-8")
print(json.dumps({"success": True, "captured": payload}))
