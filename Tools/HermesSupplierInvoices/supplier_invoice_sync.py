#!/usr/bin/env python3
"""Hermes script-only entrypoint; install under ~/.hermes/scripts when activated."""
import os
from pathlib import Path
import subprocess
import sys

root = Path(os.environ.get("CCV_SUPPLIER_WORKER_HOME", "/srv/ai/hermes/supplier-invoices"))
# Only filenames/paths are passed in arguments. Keys remain in protected files.
command = [str(root / "venv/bin/python"), str(root / "supplier_invoices.py"), "--config", str(root / "config.json")]
try:
    sys.exit(subprocess.call(command))
except OSError:
    print('{"status":"blocked","reason":"supplier_worker_not_installed"}')
    sys.exit(2)
