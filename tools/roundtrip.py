"""Exact round-trip test over all samples + plaintext structure probe."""
import sys,glob,json,os; sys.path.insert(0,os.path.dirname(__file__))
import keys, ff2save as F
cfg=keys.load(); ok=bad=0
print("EXACT ROUND TRIP  encrypt(fix_checksums(decrypt(file))) == file")
for p in sorted(glob.glob("samples/*/*/*.BIN")):
    raw=open(p,"rb").read(); hdr,pt=F.decrypt(raw,cfg)
    rt=F.encrypt(F.fix_checksums(hdr,pt,cfg),pt,cfg); same=rt==raw
    diff=[i for i in range(len(raw)) if raw[i]!=rt[i]][:5] if not same else []
    ok+=same; bad+=not same
    print(f"  {'PASS' if same else 'FAIL'} {'SYS ' if 'SYSTEM' in p else 'USER'} {p[8:]}  loader-problems={F.verify(raw,cfg) or 'none'} {diff}")
print("pass",ok,"fail",bad)
for p in ["samples/mine/SAVEDATA00/SAVEDATA.BIN","samples/mine/SAVEDATA00/BACKUP.BIN","samples/source-A/SYSTEMSAVEDATA00/SAVEDATA.BIN"]:
    raw=open(p,"rb").read(); hdr,pt=F.decrypt(raw,cfg); body=pt[16:]; end=len(body.rstrip(b"\0"))
    try: j=json.loads(body[:end].decode("utf-8")); ks=list(j.keys()) if isinstance(j,dict) else type(j)
    except Exception as e: ks=f"JSON parse failed: {e}"
    print(p[8:],"| pt[:16]=",pt[:16].hex(),"| json bytes",end,"of",len(body),"| trailing zero",len(body)-end,"| top keys",ks)
