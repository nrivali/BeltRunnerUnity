"""Verify staged Blender GLBs before replacing Unity resources. Standard library only."""
import hashlib,json,struct
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'ArtSource/AsteroidsV4'
SHAPES='lumpy chunk potato shard pancake cratered cluster slab spindle bean boulder jagged wedge hollow'.split()
report=json.loads((OUT/'mesh-report.json').read_text())['models']
assert len(report)==84
assert all(m['nonmanifold_edges']==0 and m['volume']>0 for m in report)
assert {m['concept'] for m in report}=={'fractured','layered','weathered'}
results=[]
for lod in range(3):
    name=f'asteroids_lod{lod}.glb';p=OUT/'export'/name;data=p.read_bytes()
    magic,version,size=struct.unpack_from('<III',data)
    assert magic==0x46546c67 and version==2 and size==len(data)
    length,kind=struct.unpack_from('<II',data,12);assert kind==0x4e4f534a
    doc=json.loads(data[20:20+length]);wanted={f'{s}_{v}_LOD{lod}' for s in SHAPES for v in 'AB'}
    assert {n['name'] for n in doc['nodes'] if 'mesh' in n}==wanted
    assert len(doc['meshes'])==28
    counts=[]
    for node in doc['nodes']:
        if 'mesh' not in node:continue
        mesh=doc['meshes'][node['mesh']];ps=mesh['primitives']
        assert len(ps)==2
        assert [doc['materials'][a['material']]['name'] for a in ps]==['Barren_Regolith','Ore_iron']
        for a in ps:
            assert all(key in a['attributes'] for key in ('POSITION','NORMAL','TANGENT','TEXCOORD_0'))
        triangles=sum(doc['accessors'][a['indices']]['count']//3 for a in ps)
        assert triangles==next(m['triangles'] for m in report if m['name']==node['name'])
        counts.append(triangles)
    for m in doc['materials']:
        assert all(x==0 for x in m.get('emissiveFactor',[0,0,0])), 'Intact ore must not emit light'
    assert len(data)<100*1024*1024
    old=ROOT/'Assets/Resources/Models'/name
    result=dict(file=name,bytes=len(data),sha256=hashlib.sha256(data).hexdigest(),previous_sha256=hashlib.sha256(old.read_bytes()).hexdigest(),models=28,triangles_min=min(counts),triangles_max=max(counts),triangles_total=sum(counts))
    results.append(result);print(json.dumps(result))
(OUT/'export-validation.json').write_text(json.dumps(results,indent=2))
print('PASS: all 84 concept meshes; all 3 LODs; stone/ore slots, UVs, normals, tangents and zero emission.')
