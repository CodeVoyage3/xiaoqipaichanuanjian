import sys, pathlib, tempfile, uuid, sqlite3, shutil, hashlib, json
root = pathlib.Path(sys.argv[2]).resolve()
temp = pathlib.Path(tempfile.gettempdir()).resolve()
assert root.parent == temp and str(uuid.UUID(root.name)) == root.name.lower(), 'TEMP/GUID required'
assert not root.is_symlink()
db = root / 'data' / 'app.db'
for parent in [root, db.parent, db]:
    assert not parent.is_symlink()
if sys.argv[1] == 'seed':
    con = sqlite3.connect(db)
    con.execute('pragma foreign_keys=on')
    payload = b'S9T02 synthetic retained workbook bytes\x00\x01'
    digest = hashlib.sha256(payload).hexdigest()
    con.execute("insert into imports(source_file_name,source_file_sha256,parsed_at_utc,status,product_count,batch_count,new_product_count,new_batch_count,updated_batch_count,issue_count,unsupported_category_count,new_task_product_count) values(?,?,?,'preview',0,0,0,0,0,0,0,0)", ('synthetic-retention.xlsx', digest, '2026-09-04 00:00:00'))
    key = con.execute('select last_insert_rowid()').fetchone()[0]
    con.execute('insert into import_workbooks(import_id,original_file_name,content,sha256,saved_at_utc) values(?,?,?,?,?)', (key,'synthetic-retention.xlsx',payload,digest,'2026-09-04 00:00:00'))
    con.execute('update settings set reminder_minute_of_day=613 where id=1')
    con.commit(); con.close()
with tempfile.TemporaryDirectory(prefix='S9T02-Sol-Probe-') as scratch:
    copied = pathlib.Path(scratch) / 'app.db'
    for suffix in ['', '-wal', '-shm', '-journal']:
        source = pathlib.Path(str(db)+suffix)
        if source.exists(): shutil.copy2(source, str(copied)+suffix)
    con = sqlite3.connect(copied)
    integrity = con.execute('pragma integrity_check').fetchall()
    foreign = con.execute('pragma foreign_key_check').fetchall()
    migration = con.execute('select MigrationId from __EFMigrationsHistory order by MigrationId').fetchall()
    full = '\n'.join(con.iterdump()).encode()
    sentinels = con.execute('select hex(content),sha256 from import_workbooks order by id').fetchall()
    result = {'integrity':integrity,'foreignKeys':foreign,'migrationIds':[r[0] for r in migration], 'fullFingerprint':hashlib.sha256(full).hexdigest(), 'workbooks':sentinels, 'settings':con.execute('select * from settings').fetchall()}
    con.close()
    print(json.dumps(result, ensure_ascii=True))
