import sys,pathlib,hashlib
import UnityPy
source=pathlib.Path(sys.argv[1]);out=pathlib.Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
assert hashlib.sha256(source.read_bytes()).hexdigest()=='7e77ef50fd0c718904af80cb13f7fbf8d4b0d53656437579cab299e774aae96c'
for obj in UnityPy.load(str(source)).objects:
 if obj.type.name!='TextAsset':continue
 asset=obj.read();name=asset.m_Name
 if pathlib.Path(name).name!=name or not name.endswith('.dll'):continue
 data=asset.m_Script.encode('utf-8','surrogateescape') if isinstance(asset.m_Script,str) else asset.m_Script
 (out/name).write_bytes(data)
