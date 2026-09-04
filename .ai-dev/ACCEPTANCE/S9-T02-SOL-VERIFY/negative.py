import pathlib,sys,tempfile,uuid,sqlite3,os
r=pathlib.Path(sys.argv[1]); assert r.parent.resolve()==pathlib.Path(tempfile.gettempdir()).resolve();uuid.UUID(r.name)
c=sqlite3.connect(r/'data'/'app.db')
mode=sys.argv[2]
if mode=='missing_history':c.execute('drop table __EFMigrationsHistory')
elif mode=='blank_version':c.execute("update __EFMigrationsHistory set ProductVersion=''")
elif mode=='wal_current':c.execute('pragma journal_mode=wal');c.execute('update settings set reminder_minute_of_day=614')
elif mode=='wal_unknown':c.execute('pragma journal_mode=wal');c.execute("insert into __EFMigrationsHistory values('99999999999999_WalFuture','10.0.10')")
else:raise ValueError(mode)
c.commit()
if mode.startswith('wal_'):os._exit(0)
c.close()
