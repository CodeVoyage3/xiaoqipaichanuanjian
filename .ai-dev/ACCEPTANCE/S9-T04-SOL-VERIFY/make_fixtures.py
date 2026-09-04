import pathlib, zipfile, io, struct, json, hashlib, sys, tempfile

root = pathlib.Path(__file__).parent
source = pathlib.Path(r'C:\Users\39037\AppData\Local\Temp\df25bf9d-0599-42e2-8979-3c2e6c30995a\publish-final')
if len(sys.argv)>1: source = pathlib.Path(sys.argv[1]).resolve()
if not source.resolve().is_relative_to(pathlib.Path(tempfile.gettempdir()).resolve()): raise ValueError('Only an isolated TEMP publish is allowed')
fixtures = root / 'fixtures'
fixtures.mkdir(exist_ok=True)
core = ['StoreExpiryInspector.exe', 'StoreExpiryInspector.dll', 'StoreExpiryInspector.deps.json', 'StoreExpiryInspector.runtimeconfig.json']
base = [(name, (source / name).read_bytes()) for name in core]
records = []
def make(name, entries=(), mutate=None, omit=()):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for path, content in base:
            if path not in omit: archive.writestr(path, content)
        for path, content in entries: archive.writestr(path, content)
    data = stream.getvalue()
    if mutate: data = mutate(bytearray(data))
    output = fixtures / (name + '.zip')
    output.write_bytes(data)
    records.append({'name': name, 'bytes':len(data), 'sha256':hashlib.sha256(data).hexdigest()})
def mutate_u32(offset, value):
    def change(data):
        center=data.index(b'PK\x01\x02')
        struct.pack_into('<I',data,center+offset,value)
        return data
    return change
make('valid')
for name,path in [('traversal','../escape.dll'),('backslash','..\\escape.dll'),('absolute','/escape.dll'),('drive','C:/escape.dll'),('unc','//server/share.dll'),('ads','safe.dll:payload'),('reserved','CON.dll'),('trailing_space','safe.dll '),('trailing_dot','safe.dll.'),('invalid_char','a<.dll'),('empty_segment','a//b.dll'),('dot_segment','a/./b.dll'),('backup','Backup/a.dll'),('data','data/a.dll'),('logs','logs/a.dll'),('database','app.db'),('wal','app.db-wal'),('shm','app.db-shm'),('excel','business.xlsx'),('script','run.ps1'),('pfx','private.pfx')]:
    make(name,[(path,b'synthetic')])
make('duplicate_case',[('abc.dll',b'a'),('ABC.dll',b'b')])
make('duplicate_exact',[('same.dll',b'a'),('same.dll',b'b')])
make('parent_file_conflict',[('dir.dll',b'a'),('dir.dll/child.dll',b'b')])
info=zipfile.ZipInfo('link.dll');info.create_system=3;info.external_attr=(0o120777<<16)
make('symlink',[(info,b'target.dll')])
info=zipfile.ZipInfo('reparse.dll');info.create_system=0;info.external_attr=0x400
make('reparse',[(info,b'target.dll')])
info=zipfile.ZipInfo('hardlink.dll');info.create_system=3;payload=bytes(12)+b'target.dll';info.extra=struct.pack('<HH',0x000d,len(payload))+payload
make('hardlink_extra',[(info,b'')])
info=zipfile.ZipInfo('unknown.dll');info.extra=struct.pack('<HH',0x1234,4)+b'abcd'
make('unknown_extra',[(info,b'hello')])
make('ratio_bomb',[('large.dll',bytes(2*1024*1024))])
make('many_entries',[(f'f{i}.dll',b'x') for i in range(4097)])
make('declared_single_large',mutate=mutate_u32(24,268435457))
make('missing_exe',omit=['StoreExpiryInspector.exe'])
make('missing_dll',omit=['StoreExpiryInspector.dll'])
make('bad_exe', [('StoreExpiryInspector.exe',b'not a PE')],omit=['StoreExpiryInspector.exe'])
make('bad_dll', [('StoreExpiryInspector.dll',b'not a PE')],omit=['StoreExpiryInspector.dll'])
exe=bytearray((source/'StoreExpiryInspector.exe').read_bytes())
at=exe.index(struct.pack('<I',0xFEEF04BD))
struct.pack_into('<I',exe,at+12,65536)
struct.pack_into('<I',exe,at+20,65536)
exe=exe.replace('1.0.0.0'.encode('utf-16-le'),'1.0.1.0'.encode('utf-16-le'))
make('exe_version_mismatch',[('StoreExpiryInspector.exe',exe)],omit=['StoreExpiryInspector.exe'])
make('private_block',[('runtime.json',b'{"key":"-----BEGIN PRIVATE KEY-----\\nSYNTHETIC-NOT-A-REAL-KEY"}')])
make('token',[('runtime.json',b'{"token":"ghp_'+b'A'*40+b'"}')])
def crc_bad(data):
    center=data.index(b'PK\x01\x02');struct.pack_into('<I',data,center+16,0);struct.pack_into('<I',data,14,0);return data
make('bad_crc',mutate=crc_bad)
make('local_name_mismatch',mutate=lambda d: d[:30]+b'X'+d[31:])
make('trailing_junk',mutate=lambda d:d+b'junk')
make('prepended_junk',mutate=lambda d:b'junk'+d)
def unsupported(data):
    center=data.index(b'PK\x01\x02');struct.pack_into('<H',data,center+10,99);struct.pack_into('<H',data,8,99);return data
make('unsupported_compression',mutate=unsupported)
def encrypted(data):
    center=data.index(b'PK\x01\x02');struct.pack_into('<H',data,center+8,1);struct.pack_into('<H',data,6,1);return data
make('encrypted',mutate=encrypted)
def invalid_utf8(data):
    center=data.index(b'PK\x01\x02');struct.pack_into('<H',data,center+8,0x800);struct.pack_into('<H',data,6,0x800);data[30]=255;data[center+46]=255;return data
make('invalid_utf8',mutate=invalid_utf8)
# Additional raw-boundary and allowed-name secret regressions.
make('private_in_runtimeconfig', [('StoreExpiryInspector.runtimeconfig.json', b'{"key":"-----BEGIN PRIVATE KEY-----\\nSYNTHETIC-NOT-A-REAL-KEY"}')], omit=['StoreExpiryInspector.runtimeconfig.json'])
make('token_in_deps', [('StoreExpiryInspector.deps.json', b'{"token":"ghp_'+b'A'*40+b'"}')], omit=['StoreExpiryInspector.deps.json'])
make('nested_business', [('fr/Backup/a.dll', b'synthetic')])
make('control_char', [('bad\x01.dll', b'synthetic')])
def disk_count(data):
    struct.pack_into('<H', data, len(data)-22+8, 1);return data
make('disk_count_mismatch', mutate=disk_count)
def unknown_flag(data):
    center=data.index(b'PK\x01\x02');struct.pack_into('<H',data,center+8,0x40);struct.pack_into('<H',data,6,0x40);return data
make('unknown_flag',mutate=unknown_flag)
def sfx(data):
    end=len(data)-22; offset=struct.unpack_from('<I',data,end+16)[0];cur=offset
    while data[cur:cur+4]==b'PK\x01\x02':
        local=struct.unpack_from('<I',data,cur+42)[0];struct.pack_into('<I',data,cur+42,local+4)
        n,e,c=struct.unpack_from('<HHH',data,cur+28);cur+=46+n+e+c
    struct.pack_into('<I',data,end+16,offset+4)
    return b'junk'+data
make('offset_adjusted_sfx',mutate=sfx)
make('rsa_private_json', [('StoreExpiryInspector.runtimeconfig.json', b'{\"key\":\"-----BEGIN RSA PRIVATE KEY-----SYNTHETIC-NOT-A-KEY\"}')], omit=['StoreExpiryInspector.runtimeconfig.json'])
make('private_disguised_dll', [('extra.dll', b'-----BEGIN PRIVATE KEY-----\n'+b'A'*80+b'\n-----END PRIVATE KEY-----')])
make('token_disguised_dll', [('extra.dll', b'ghp_'+b'A'*40)])
make('nested_business_backslash', [('fr\\Backup\\a.dll', b'synthetic')])
make('overlong_component', [('a'*256+'.dll', b'synthetic')])
revision=bytearray((source/'StoreExpiryInspector.exe').read_bytes());revision_at=revision.index(struct.pack('<I',0xFEEF04BD));struct.pack_into('<I',revision,revision_at+12,1);struct.pack_into('<I',revision,revision_at+20,1);revision=revision.replace('1.0.0.0'.encode('utf-16-le'),'1.0.0.1'.encode('utf-16-le'))
make('exe_revision_mismatch', [('StoreExpiryInspector.exe',revision)], omit=['StoreExpiryInspector.exe'])
def eocd_comment(data):
    struct.pack_into('<H',data,len(data)-2,4);return data+b'test'
make('archive_comment', mutate=eocd_comment)
full = fixtures/'full_publish.zip'
with zipfile.ZipFile(full, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
    for item in sorted(source.rglob('*')):
        if item.is_file(): archive.write(item, item.relative_to(source).as_posix())
records.append({'name':'full_publish','bytes':full.stat().st_size,'sha256':hashlib.sha256(full.read_bytes()).hexdigest(),'files':sum(1 for item in source.rglob('*') if item.is_file())})
(root/'fixture-index.json').write_text(json.dumps({'source':str(source),'syntheticCandidateVersion':'1.0.0','injectedSourceVersion':'0.9.9','candidateExecuted':False,'fixtures':records},indent=2))
print(f'{len(records)} independent fixtures, source files only; candidate never executed')
