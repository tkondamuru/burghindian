"""Incremental-build manifest: content hashes per page."""
import hashlib
import json
from pathlib import Path


def dataset_hash(items):
    canonical = json.dumps(items, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def load(manifest_path):
    path = Path(manifest_path)
    if path.exists():
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return {}
    return {}


def save(manifest_path, data):
    path = Path(manifest_path)
    path.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
