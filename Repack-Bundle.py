import sys,pathlib,json,hashlib
root=pathlib.Path(__file__).parent
sys.path.insert(0,str(root/'py312-deps'))
import UnityPy
bundle=pathlib.Path(sys.argv[1]) if len(sys.argv)>1 else next((root/'device-collection/BetterAstralParty_Android_collection/collected/com.unity.addressables/AssetBundles').rglob('__data'))
probe_dir=pathlib.Path(sys.argv[2]) if len(sys.argv)>2 else root/'collection-analysis/probe'
patched=(probe_dir/'AstralParty.Runtime.dll').read_bytes()
env=UnityPy.load(str(bundle)); before={}
for o in env.objects:
    if o.type.name!='TextAsset':continue
    a=o.read(); raw=a.m_Script.encode('utf-8','surrogateescape') if isinstance(a.m_Script,str) else a.m_Script
    before[a.m_Name]=hashlib.sha256(raw).hexdigest()
    if a.m_Name=='AstralParty.Runtime.dll':
        a.m_Script=patched.decode('utf-8','surrogateescape'); a.save()
output=probe_dir/'runtime-probe.bundle'
output.write_bytes(env.file.save(packer='lz4'))
after={}
for o in UnityPy.load(str(output)).objects:
    if o.type.name!='TextAsset':continue
    a=o.read();raw=a.m_Script.encode('utf-8','surrogateescape') if isinstance(a.m_Script,str) else a.m_Script
    after[a.m_Name]=hashlib.sha256(raw).hexdigest()
assert before.keys()==after.keys()
changed=[n for n in before if before[n]!=after[n]]
assert changed==['AstralParty.Runtime.dll']
assert after['AstralParty.Runtime.dll']==hashlib.sha256(patched).hexdigest()
report={'input_bundle_sha256':hashlib.sha256(bundle.read_bytes()).hexdigest(),'output_bundle_sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'output_size':output.stat().st_size,'text_assets_checked':len(before),'changed_text_assets':changed,'unchanged_text_assets':len(before)-len(changed),'runtime_dll_sha256':after['AstralParty.Runtime.dll'],'runtime_tested':False,'deployment_performed':False}
(probe_dir/'bundle-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
