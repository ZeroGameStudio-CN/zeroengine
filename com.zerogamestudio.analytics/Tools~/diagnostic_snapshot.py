"""Decode bounded diagnostic snapshots from an authorized Analytics event-search JSON response.

This utility performs no network calls and reads no game files or credentials.
Usage: python diagnostic_snapshot.py <response.json> [--incident <32-hex-id>]
"""
import argparse
import base64
import gzip
import hashlib
import io
import json
from pathlib import Path

MAX_JSON_BYTES = 8 * 1024 * 1024
MAX_SNAPSHOT_BYTES = 128 * 1024
MAX_COMPRESSED_BYTES = 7 * 1024


def decode(props):
    if isinstance(props, str):
        props = json.loads(props)
    if props.get("snapshot_encoding") != "gzip-base64":
        raise ValueError("Unsupported snapshot encoding")
    encoded = props.get("snapshot_data", "")
    if not isinstance(encoded, str) or len(encoded) > 4 * ((MAX_COMPRESSED_BYTES + 2) // 3):
        raise ValueError("Oversized compressed snapshot")
    compressed = base64.b64decode(encoded, validate=True)
    with gzip.GzipFile(fileobj=io.BytesIO(compressed)) as stream:
        raw = stream.read(MAX_SNAPSHOT_BYTES + 1)
    if len(raw) > MAX_SNAPSHOT_BYTES or len(raw) != props.get("snapshot_bytes"):
        raise ValueError("Snapshot size does not match receipt")
    if hashlib.sha256(raw).hexdigest() != props.get("snapshot_sha256"):
        raise ValueError("Snapshot SHA-256 does not match receipt")
    return {
        "diagnostic_id": props["diagnostic_id"],
        "revision": props["diagnostic_revision"],
        "kind": props["diagnostic_kind"],
        "context": props.get("context", {}),
        "snapshot": json.loads(raw),
    }


def snapshots(response, incident=None):
    result = []
    def walk(value, depth=0):
        if depth > 16:
            raise ValueError("Response nesting exceeds limit")
        if isinstance(value, dict):
            if value.get("event") == "diagnostic_incident" and "props" in value:
                if value.get("props_detail_truncated"):
                    raise ValueError("Analytics returned a truncated event; this is not a complete snapshot")
                props = value["props"]
                if isinstance(props, str):
                    props = json.loads(props)
                if incident is None or props.get("diagnostic_id") == incident:
                    result.append(decode(props))
                    if len(result) > 200:
                        raise ValueError("Too many snapshots; narrow the Analytics query")
            else:
                for child in value.values():
                    walk(child, depth + 1)
        elif isinstance(value, list):
            for child in value:
                walk(child, depth + 1)
    walk(response)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("response", type=Path)
    parser.add_argument("--incident")
    args = parser.parse_args()
    if args.response.stat().st_size > MAX_JSON_BYTES:
        raise ValueError("Response exceeds the Analytics response limit")
    response = json.loads(args.response.read_text(encoding="utf-8-sig"))
    print(json.dumps(snapshots(response, args.incident), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
