# worklist.py <scratch>: intersect the classes and structs the reference packages use with the engine and project types
# whose native serializers write data, and write the work list
import os, sys, collections
S = sys.argv[1] + '/inv'
mod_cache = {}
def module_of(path):
    d = os.path.dirname(path)
    while d and d != '/':
        if d in mod_cache: return mod_cache[d]
        try: builds = [f for f in os.listdir(d) if f.endswith('.Build.cs')]
        except OSError: builds = []
        if builds:
            m = builds[0][:-len('.Build.cs')]
            mod_cache[d] = m
            return m
        d = os.path.dirname(d)
    return None

parent = {}            # C++ class name -> C++ parent name
module = {}            # C++ class name -> module
for line in open(f'{S}/inherit.tsv'):
    c, p, h = line.rstrip('\n').split('\t')
    parent.setdefault(c, p); module.setdefault(c, module_of(h))
for line in open(f'{S}/../all-headers.lst'):
    pass
serializes = collections.defaultdict(set)   # C++ class -> archive kinds
where = {}
for line in open(f'{S}/serialize-defs.tsv'):
    f = line.rstrip('\n').split('\t')
    if len(f) != 2 or ' ' not in f[0]: continue
    sig, path = f
    c, kind = sig.split(' ', 1)
    if c[0] in 'UA': serializes[c].add(kind); where.setdefault(c, path)
for line in open(f'{S}/project-serialize.tsv'):
    if not line.strip(): continue
    c, kind, path = line.rstrip('\n').split('\t')
    serializes[c].add(kind); where.setdefault(c, path)
proj_parent = {}
for line in open(os.path.expanduser('~/code/scratch/uasset-dump/mappings/project-parents.tsv')):
    f = line.rstrip('\n').split('\t')
    if len(f) == 2: proj_parent[f[0]] = f[1]

# UObjectBase::RemoveClassPrefix: the reflected name drops the U/A prefix and a DEPRECATED_ prefix
by_reflected = {}
for c in list(parent) + list(serializes):
    r = c[1:]
    if r.startswith('DEPRECATED_'): r = r[len('DEPRECATED_'):]
    by_reflected.setdefault(r, c)
# [CoreRedirects] ClassRedirects, keyed by the old /Script/Module.Name or bare name
redirects = {}
for line in open(f'{S}/class-redirects.tsv'):
    f = line.rstrip('\n').split('\t')
    if len(f) == 2: redirects[f[0]] = f[1]
def cpp_names(name):
    return [x for x in ('U' + name, 'A' + name, by_reflected.get(name)) if x]

used_classes = {}; used_structs = {}
for line in open(f'{S}/usage.tsv'):
    f = line.rstrip('\n').split('\t')
    if f[0] == 'class' and f[1].startswith('/Script/'): used_classes[(f[1][8:], f[2])] = (int(f[3]), f[4])
    elif f[0] in ('struct-name', 'struct-import'): used_structs[f[2]] = (int(f[3]), f[4])

items = {}   # serializer-defining C++ class -> (packages, example, used classes reaching it)
unresolved = []
for (mod, name), (n, ex) in used_classes.items():
    new = redirects.get(f'/Script/{mod}.{name}') or redirects.get(name)
    if new: name = new.split('.')[-1]
    c = next((x for x in cpp_names(name) if x in parent or x in serializes), None)
    chain = []
    if c is None:
        # project class: declared parents by reflected name
        x = name
        for _ in range(64):
            if x not in proj_parent: break
            x = proj_parent[x]; chain.append(x)
        c = next((y for y in cpp_names(chain[-1]) if y in parent), None) if chain else None
        if c is None: unresolved.append((mod, name, n)); continue
    for _ in range(64):
        if c in serializes:
            it = items.setdefault(c, [0, ex, set()]); it[0] += n; it[2].add(name)
        if c not in parent: break
        c = parent[c]
with open(f'{S}/worklist-classes.tsv', 'w') as o:
    for c, (n, ex, users) in sorted(items.items(), key=lambda kv: -kv[1][0]):
        o.write(f"{c}\t{module.get(c) or ''}\t{','.join(sorted(serializes[c]))}\t{n}\t{len(users)}\t{where.get(c,'')}\t{ex}\n")
native = {}
for fn in ('native-structs.tsv', 'project-native-structs.tsv'):
    for line in open(f'{S}/{fn}'):
        f = line.rstrip('\n').split('\t')
        if len(f) >= 3: native[f[0][1:]] = (f[1], f[2])
core = set(open(f'{S}/struct-names.txt').read().split()) - set(native)
with open(f'{S}/worklist-structs.tsv', 'w') as o:
    for s, (n, ex) in sorted(used_structs.items(), key=lambda kv: -kv[1][0]):
        if s in native: o.write(f"{s}\t{native[s][0]}\t{n}\t{native[s][1]}\t{ex}\n")
        elif s in core: o.write(f"{s}\tcore\t{n}\t\t{ex}\n")
with open(f'{S}/unresolved-classes.tsv', 'w') as o:
    for u in sorted(unresolved, key=lambda u: -u[2]): o.write('\t'.join(map(str, u)) + '\n')
print('classes used', len(used_classes), 'serializer classes reached', len(items), 'unresolved', len(unresolved))
print('structs reached', sum(1 for _ in open(f'{S}/worklist-structs.tsv')))
