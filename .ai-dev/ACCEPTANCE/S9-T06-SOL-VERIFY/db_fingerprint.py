"""Independent read-only audit of closed synthetic SQLite evidence in TEMP/GUID."""
import hashlib
import json
import pathlib
import sqlite3
import sys
import tempfile
import uuid


def digest(value):
    return hashlib.sha256(json.dumps(value, ensure_ascii=True, separators=(",", ":")).encode()).hexdigest()


def cell(value):
    if value is None:
        return ["null"]
    if isinstance(value, bytes):
        return ["blob", len(value), hashlib.sha256(value).hexdigest()]
    if isinstance(value, float):
        return ["real", value.hex()]
    return ["integer" if isinstance(value, int) else "text", value]


def audit(filename):
    path = pathlib.Path(filename).absolute()
    temp = pathlib.Path(tempfile.gettempdir()).resolve()
    relative = path.relative_to(temp)
    uuid.UUID(relative.parts[0])
    for item in [path, *path.parents]:
        if item.is_symlink() or item.is_junction():
            raise ValueError("Evidence path contains a link or junction")
    if not path.is_file():
        raise ValueError("Existing synthetic evidence file required")
    for suffix in ("-wal", "-journal"):
        sidecar = pathlib.Path(str(path) + suffix)
        if sidecar.exists() and sidecar.stat().st_size:
            raise ValueError("Checkpoint and close the synthetic application before collecting evidence")
    before = hashlib.sha256(path.read_bytes()).hexdigest()
    connection = sqlite3.connect(path.as_uri() + "?mode=ro&immutable=1", uri=True)
    try:
        integrity = [r[0] for r in connection.execute("PRAGMA integrity_check")]
        foreign_keys = connection.execute("PRAGMA foreign_key_check").fetchall()
        schema = connection.execute("SELECT type,name,tbl_name,sql FROM sqlite_schema ORDER BY type,name").fetchall()
        migrations = [r[0] for r in connection.execute("SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId")]
        tables = {}
        for (name,) in connection.execute("SELECT name FROM sqlite_schema WHERE type='table' ORDER BY name"):
            quoted = '"' + name.replace('"', '""') + '"'
            cursor = connection.execute("SELECT * FROM " + quoted)
            columns = [c[0] for c in cursor.description]
            rows = sorted(digest([cell(v) for v in row]) for row in cursor)
            tables[name] = {"columns": columns, "rowCount": len(rows), "rows": rows, "fingerprint": digest([columns, rows])}
    finally:
        connection.close()
    unchanged = before == hashlib.sha256(path.read_bytes()).hexdigest()
    healthy = integrity == ["ok"] and not foreign_keys and len(migrations) == 9 and migrations[-1] == "20260901155124_AddPolicyAndBaselineFoundation"
    return {"syntheticOnly": True, "sourceBytesUnchanged": unchanged, "databaseSha256": before,
            "integrity": integrity, "foreignKeys": foreign_keys, "migrations": migrations,
            "schemaFingerprint": digest(schema), "tables": tables,
            "fullFieldBlobFingerprint": digest([schema, tables]), "healthy": healthy}


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-check"]:
        assert cell(b"\0\xff") != cell("\0\xff")
        assert digest([cell(1)]) != digest([cell(1.0)])
        assert digest([cell(b"a")]) != digest([cell(b"b")])
        print("PASS: lossless type distinctions and BLOB changes affect fingerprints")
    else:
        result = audit(sys.argv[1])
        print(json.dumps(result, ensure_ascii=True))
        sys.exit(0 if result["healthy"] and result["sourceBytesUnchanged"] else 1)
