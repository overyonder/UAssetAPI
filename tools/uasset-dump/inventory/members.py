# members.py <Struct> <field>...: the declaration line of each field inside the struct/class body
import sys,re,subprocess,os
E=os.path.expanduser('~/.local/opt/unreal-engine/5.8.3/Engine')
st=sys.argv[1]; fields=sys.argv[2:]
files=subprocess.run(['rg','--color','never','-l','-g','*.h',r'(struct|class)\s+(\w+_API\s+)?'+st+r'\b[^;]*$',E+'/Source',E+'/Plugins'],capture_output=True,text=True).stdout.split()
for f in files:
    t=open(f,errors='ignore').read()
    for m in re.finditer(r'(struct|class)\s+(\w+_API\s+)?'+st+r'\b[^;{]*\{',t):
        i=m.end(); depth=1; j=i
        while depth and j<len(t):
            if t[j]=='{': depth+=1
            elif t[j]=='}': depth-=1
            j+=1
        body=t[i:j]
        for fl in fields:
            for l in body.splitlines():
                if re.search(r'\b'+fl+r'\b\s*(;|=|\{|\[|:)',l) and not re.search(r'\(.*\b'+fl+r'\b',l) and 'return' not in l:
                    print(st,fl,'|',l.strip()[:150])
