"""Restore this captured database and blobs into a NEW directory (Python 3.11+).

Usage: python restore_snapshot.py C:/path/to/new/recovery-directory
Never points at or overwrites the running server database.
"""
import hashlib
import json
import pathlib
import sqlite3
import sys


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def within(root, relative):
    result = (root / relative).resolve()
    if not result.is_relative_to(root.resolve()):
        raise ValueError(f"Path outside snapshot directory: {relative}")
    return result


def identifier(value):
    return '"' + value.replace('"', '""') + '"'


def decode(value, root):
    if not isinstance(value, dict):
        return value
    content = within(root, value["object"]).read_bytes()
    return content.decode("utf-8") if value["type"] == "text" else content


def encode_for_verification(value):
    if isinstance(value, bytes):
        kind, content, extension = "blob", value, "bin"
    elif isinstance(value, str) and len(value.encode("utf-8")) > 2048:
        kind, content, extension = "text", value.encode("utf-8"), "txt"
    else:
        return value
    hashed = hashlib.sha256(content).hexdigest()
    return {"type": kind, "object": f"objects/{hashed[:2]}/{hashed}.{extension}"}


def restore(root, destination):
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    if manifest["formatVersion"] != 1:
        raise ValueError("Unsupported snapshot format")
    if destination.exists():
        raise FileExistsError("Recovery requires a new directory; nothing was overwritten")
    for record in manifest["files"]:
        path = within(root, record["path"])
        if path.stat().st_size != record["bytes"] or digest(path) != record["sha256"]:
            raise ValueError(f"Snapshot file failed verification: {record['path']}")
    schema = json.loads((root / "schema.json").read_text(encoding="utf-8"))
    destination.mkdir(parents=True)
    database = destination / "dantesroleplay.db"
    with sqlite3.connect(database) as connection:
        connection.execute("PRAGMA foreign_keys=OFF")
        # Replay historical state exactly, including the source's reserved 'system' row.
        # Its retained CHECK constraint forbids new inserts; this is not a data repair.
        connection.execute("PRAGMA ignore_check_constraints=ON")
        connection.execute(f"PRAGMA user_version={int(manifest['userVersion'])}")
        connection.execute(f"PRAGMA application_id={int(manifest['applicationId'])}")
        shadows = {t["name"] for t in manifest["tables"] if t["tableType"] == "shadow"}
        # Virtual table creation owns its shadow schema. Indexes and triggers follow data.
        for item in schema:
            if item["type"] == "table" and item["name"] not in shadows and not item["name"].startswith("sqlite_"):
                connection.execute(item["sql"])

        def load_table(table):
            if table["tableType"] == "virtual":
                return  # Its complete storage is captured in the shadow tables.
            name = identifier(table["name"])
            connection.execute(f"DELETE FROM {name}")
            columns = table["columns"]
            query = f"INSERT INTO {name} ({','.join(identifier(c) for c in columns)}) VALUES ({','.join('?' for _ in columns)})"
            count = 0
            logical = hashlib.sha256()
            for part in table["parts"]:
                with within(root, part).open("rb") as stream:
                    batch = []
                    for line in stream:
                        logical.update(line)
                        batch.append(tuple(decode(v, root) for v in json.loads(line)))
                        count += 1
                        if len(batch) == 1000:
                            connection.executemany(query, batch)
                            batch.clear()
                    if batch:
                        connection.executemany(query, batch)
            if count != table["rows"] or logical.hexdigest() != table["rowSha256"]:
                raise ValueError(f"Row count or digest mismatch: {table['name']}")

        for table in manifest["tables"]:
            if not table["name"].startswith("sqlite_"):
                load_table(table)
        for item in schema:
            if item["type"] == "index" and item["sql"]:
                connection.execute(item["sql"])
        if any(t["name"] == "sqlite_stat1" for t in manifest["tables"]):
            connection.execute("ANALYZE")
        for table in manifest["tables"]:
            if table["name"].startswith("sqlite_"):
                load_table(table)
        for item in schema:
            if item["type"] in ("trigger", "view"):
                connection.execute(item["sql"])
        connection.commit()
        connection.execute("PRAGMA ignore_check_constraints=OFF")
    # Reopen after restoring FTS shadow tables so no transient virtual-table state survives.
    with sqlite3.connect(database) as connection:
        actual_schema = [dict(zip(("type", "name", "table", "sql"), row)) for row in connection.execute(
            "SELECT type,name,tbl_name,sql FROM sqlite_master ORDER BY type,name")]
        if actual_schema != schema:
            raise ValueError("Restored schema differs from the captured schema")
        integrity = [r[0] for r in connection.execute("PRAGMA integrity_check")]
        if integrity != manifest["sourceIntegrityCheck"]:
            raise ValueError(f"Integrity differs from source: {integrity}")
        connection.execute("PRAGMA ignore_check_constraints=ON")
        structural = [r[0] for r in connection.execute("PRAGMA integrity_check")]
        connection.execute("PRAGMA ignore_check_constraints=OFF")
        if structural != ["ok"]:
            raise ValueError(f"Structural integrity check failed: {structural}")
        for table in manifest["tables"]:
            logical = hashlib.sha256()
            count = 0
            for row in connection.execute(table["query"]):
                encoded = [encode_for_verification(v) for v in row]
                logical.update((json.dumps(encoded, ensure_ascii=False, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8"))
                count += 1
            if count != table["rows"] or logical.hexdigest() != table["rowSha256"]:
                raise ValueError(f"Restored row content/count mismatch: {table['name']}")
        foreign_keys = [list(row) for row in connection.execute("PRAGMA foreign_key_check")]
        if sorted(foreign_keys, key=repr) != sorted(manifest["foreignKeyCheck"], key=repr):
            raise ValueError("Foreign-key state differs from the source snapshot")
    for blob in manifest["externalBlobs"]:
        target = within(destination / "blobs", blob["path"])
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(within(root, blob["object"]).read_bytes())
        if digest(target) != blob["sha256"]:
            raise ValueError(f"Restored blob mismatch: {blob['path']}")
    print(json.dumps({"database": str(database), "sourceIntegrityPreserved": integrity,
                      "structuralIntegrity": structural, "schemaAndAllRowsMatch": True,
                      "tables": len(manifest["tables"]), "externalBlobs": len(manifest["externalBlobs"])}))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python restore_snapshot.py <NEW recovery directory>")
    restore(pathlib.Path(__file__).resolve().parent, pathlib.Path(sys.argv[1]).resolve())
