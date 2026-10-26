# vers.py <Struct::Member>...: ordinal of each version enum member and the struct's GUID, from the engine source
import sys,re,subprocess,os
E=os.path.expanduser('~/.local/opt/unreal-engine/5.8.3/Engine')
cache={}
def find(struct):
    if struct in cache: return cache[struct]
    out=subprocess.run(['rg','--color','never','-l','-g','*.h','-g','*.cpp',r'(struct|namespace)\s+(\w+_API\s+)?'+struct+r'\b\s*(\{|$)',E+'/Source',E+'/Plugins'],capture_output=True,text=True).stdout.split()
    cache[struct]=out; return out
def ordinal(text,struct,member):
    i=text.find(struct)
    while i>=0:
        seg=text[i:i+200000]
        m=re.search(r'enum\s+(?:class\s+)?\w*\s*(?::\s*\w+\s*)?\{(.*?)\}',seg,re.S)
        if m and member in m.group(1):
            body=re.sub(r'//.*','',m.group(1)); body=re.sub(r'/\*.*?\*/','',body,flags=re.S)
            v=-1
            for e in [x.strip() for x in body.split(',') if x.strip()]:
                if '=' in e:
                    n,val=[y.strip() for y in e.split('=',1)]
                    try: v=int(val,0)
                    except: v=None
                else: n=e; v=v+1 if v is not None else None
                if n==member: return v
        i=text.find(struct,i+1)
    return None
HEX=r'(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)'
def guid_any(struct):
    out=subprocess.run(['rg','--color','never','-o','-N','--no-filename','-U','-g','*.h','-g','*.cpp',struct+r'::GUID\s*(?:\(|=\s*FGuid\s*\(|\{)\s*'+HEX,E+'/Source',E+'/Plugins'],capture_output=True,text=True).stdout
    m=re.search(HEX,out)
    if m: return ','.join(m.groups())
    for f in find(struct):
        t=open(f,errors='ignore').read(); i=t.find(struct)
        m=re.search(r'GUID[^;]{0,40}?'+HEX,t[i:i+4000],re.S) if i>=0 else None
        if m: return ','.join(m.groups())
    return None
def inl(struct,member):
    out=subprocess.run(['rg','--color','never','-N','-g','*.inl',r'\(\s*'+member+r'\s*,\s*(\d+)\s*\)',E+'/Source',E+'/Plugins'],capture_output=True,text=True).stdout.splitlines()
    for l in out:
        f,_,rest=l.partition(':')
        stem=struct.lstrip('F').replace('Object','').replace('Version','').replace('Custom','')
        m=re.search(r'\(\s*'+member+r'\s*,\s*(\d+)',rest)
        if m: return int(m.group(1)), f
    return None, None
def guid(text,struct):
    m=re.search(struct+r'::GUID\s*\(\s*(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)',text) or re.search(r'GUID\s*\(\s*(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)\s*,\s*(0x[0-9A-Fa-f]+)',text)
    return ','.join(m.groups()) if m else None
for a in sys.argv[1:]:
    s,mem=a.split('::')
    res=None;g=None;src=None
    for f in find(s):
        t=open(f,errors='ignore').read()
        r=ordinal(t,s,mem)
        if r is not None: res=r; src=f; break
    if res is None: res,src=inl(s,mem)
    g=guid_any(s)
    print(f'{a}\t{res}\t{g}\t{src.split("Engine/")[-1] if src else ""}')
