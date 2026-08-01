from __future__ import annotations

import subprocess
import sys
from pathlib import Path

server = Path(__file__).with_name("fake_upstream_server.py")
kwargs = {}
if sys.platform == "win32":
    kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
child = subprocess.Popen([sys.executable, str(server), *sys.argv[1:]], **kwargs)
try:
    raise SystemExit(child.wait())
finally:
    if child.poll() is None:
        child.terminate()
        child.wait(timeout=5)
