# versions.py <engine> <name=header>...: each custom version's GUID and its enum members' values, from the header that
# declares it (members counted in order, honouring explicit values; #if blocks are not expected in these enums)
import re, sys, os, subprocess
eng = sys.argv[1]
for arg in sys.argv[2:]:
    name, path = arg.split('=', 1)
    s = open(os.path.join(eng, path), errors='replace').read()
    m = re.search(r'(?:struct|namespace)\s+(?:\w+_API\s+)?' + name + r'\b(.*?)\n\};', s, re.S)
    body = m.group(1) if m else s
    e = re.search(r'enum\s+(?:class\s+)?Type[^{]*\{(.*?)\}', body, re.S)
    members = {}
    if e:
        t = re.sub(r'//[^\n]*|/\*.*?\*/', '', e.group(1), flags=re.S)
        if '#if' in t: print(name, 'WARNING: #if in enum')
        v = -1
        for item in [x.strip() for x in t.split(',') if x.strip()]:
            mm = re.match(r'(\w+)\s*(?:=\s*(.+))?$', item, re.S)
            if not mm: continue
            k, val = mm.groups()
            if val is not None:
                val = val.strip()
                v = int(val, 0) if re.match(r'^-?(0x)?[0-9a-fA-F]+$', val) else members.get(val.split('-')[0].strip(), v) - (1 if '- 1' in val else 0)
            else: v += 1
            members[k] = v
    g = re.search(name + r'::GUID\s*\(\s*(0x\w+)\s*,\s*(0x\w+)\s*,\s*(0x\w+)\s*,\s*(0x\w+)', s) or re.search(r'GUID\s*\(\s*(0x\w+)\s*,\s*(0x\w+)\s*,\s*(0x\w+)\s*,\s*(0x\w+)', body)
    if not g:
        out = subprocess.run(['rg', '--no-config', '-o', '-h', '-g', '*.cpp', name + r'::GUID\s*\(\s*0x\w+\s*,\s*0x\w+\s*,\s*0x\w+\s*,\s*0x\w+', os.path.join(eng, 'Source'), os.path.join(eng, 'Plugins')], capture_output=True, text=True).stdout
        g = re.search(r'\(\s*(0x\w+)\s*,\s*(0x\w+)\s*,\s*(0x\w+)\s*,\s*(0x\w+)', out)
    print(name, 'GUID', ','.join(g.groups()) if g else None)
    print('   ', ' '.join(f'{k}={v}' for k, v in members.items()))
