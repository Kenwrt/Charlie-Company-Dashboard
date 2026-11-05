"""Optional script-only Hermes launcher; not installed, scheduled or activated by source preparation."""
import subprocess
import sys

if __name__ == "__main__":
    root = "/srv/ai/hermes/home-depot-history"
    sys.exit(subprocess.call([root + "/venv/bin/python", root + "/home_depot_history.py", "--config", root + "/config.json", "--collect", "--use-current-range", "--collect-only"]))
