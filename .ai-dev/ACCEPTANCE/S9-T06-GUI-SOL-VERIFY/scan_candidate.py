import hashlib
import json
import pathlib
import re
import sys
import zipfile

candidate = pathlib.Path(sys.argv[1])
report = pathlib.Path(sys.argv[2])
patterns = {
    "private-pem": rb"-----BEGIN (?:RSA |EC |ENCRYPTED )?PRIVATE KEY-----[\r\n\t A-Za-z0-9+/=]{100,}-----END (?:RSA |EC |ENCRYPTED )?PRIVATE KEY-----",
    "github-token": rb"(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})",
    "local-user-path": rb"[Cc]:[\\/]Users[\\/]39037[\\/]",
}
hits = []
guard_markers = []
entries = []
with zipfile.ZipFile(candidate) as archive:
    for entry in archive.infolist():
        if entry.is_dir():
            continue
        data = archive.read(entry)
        entries.append({"name": entry.filename, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
        if pathlib.PurePosixPath(entry.filename).suffix.lower() in {".db", ".pfx", ".p12", ".key"}:
            hits.append({"name": entry.filename, "kind": "forbidden-file"})
        for kind, pattern in patterns.items():
            if re.search(pattern, data) or re.search(pattern, data.replace(b"\x00", b"")):
                hits.append({"name": entry.filename, "kind": kind})
        if re.search(rb"-----BEGIN (?:RSA |EC |ENCRYPTED )?PRIVATE KEY-----", data.replace(b"\x00", b"")):
            if entry.filename == "app/StoreExpiryInspector.dll":
                guard_markers.append({"name": entry.filename, "source": "SignedUpdatePackageDownloader.ContainsSecret guard string literals; independently reviewed; no PEM payload"})
            else:
                hits.append({"name": entry.filename, "kind": "unreviewed-private-marker"})
report.write_text(json.dumps({"candidateBytes": candidate.stat().st_size, "candidateSha256": hashlib.sha256(candidate.read_bytes()).hexdigest(), "checkedEntries": len(entries), "knownSecretHits": hits, "reviewedGuardMarkers": guard_markers, "limitation": "Known patterns and containers; not a proof against every encoding", "entries": entries}, indent=2), encoding="utf-8")
print(json.dumps({"entries": len(entries), "knownSecretHits": len(hits), "zipSha256": hashlib.sha256(candidate.read_bytes()).hexdigest()}))
sys.exit(bool(hits))
