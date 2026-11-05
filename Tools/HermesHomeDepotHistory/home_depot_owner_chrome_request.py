"""Hermes 07:00 dispatcher; the local Codex browser worker owns Chrome actions."""
import subprocess
import sys

if __name__ == '__main__':
    root = '/srv/ai/hermes/home-depot-history'
    sys.exit(subprocess.call([root + '/venv/bin/python', root + '/owner_chrome_dispatch.py', 'enqueue']))
