"""Private, replay-safe original archiving for the Home Depot Analyst agent."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import sqlite3
from home_depot_history import atomic, private_directory, single_run, MAX_BYTES


def main():
    root = Path(__file__).resolve().parent
    config = json.loads((root / "config.json").read_text(encoding="utf-8"))
    state = private_directory(config["state_directory"])
    downloads = private_directory(config["downloads_directory"])
    inbox = private_directory(config["inbox_directory"])
    with single_run(state), sqlite3.connect(state / "archive.sqlite") as db:
        db.execute("create table if not exists originals (sha256 text primary key, kind text not null, bytes integer not null, first_seen_at text not null)")
        if os.name != "nt":
            (state / "archive.sqlite").chmod(0o600)
        added = 0
        repeated = 0
        now = dt.datetime.now(dt.timezone.utc).isoformat()
        for source in sorted(downloads.iterdir()):
            if not source.is_file() or source.is_symlink() or source.suffix.lower() not in (".csv", ".pdf"):
                continue
            if source.stat().st_size > MAX_BYTES:
                raise RuntimeError("Original exceeds supported size; retained privately for review")
            data = source.read_bytes()
            kind = "Csv" if source.suffix.lower() == ".csv" else "Receipt"
            if kind == "Csv" and b"Date,Store Number," not in data[:65536]:
                # Summary exports are never silently accepted as detailed history.
                raise RuntimeError("A downloaded CSV is not the supported detailed purchase-history export")
            if kind != "Csv" and not data.startswith(b"%PDF-"):
                raise RuntimeError("A downloaded receipt is not PDF evidence")
            digest = hashlib.sha256(data).hexdigest()
            target = inbox / (digest + source.suffix.lower())
            if not target.exists():
                atomic(target, data)
            existing = db.execute("select 1 from originals where sha256=?", (digest,)).fetchone()
            if existing:
                repeated += 1
            else:
                db.execute("insert into originals values (?, ?, ?, ?)", (digest, kind, len(data), now))
                added += 1
            db.commit()
        receipt = {"at": now, "new_originals": added, "already_archived": repeated,
                   "total_originals": db.execute("select count(*) from originals").fetchone()[0],
                   "dashboard_handoff": "Deferred", "complete_history": False}
        atomic(state / "last-archive.json", json.dumps(receipt).encode())
        print(json.dumps(receipt))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print(json.dumps({"status": "ArchiveIncomplete", "originals_retained": True, "complete_history": False}))
        raise SystemExit(1)
