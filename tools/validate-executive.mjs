import fs from 'node:fs';import path from 'node:path';import cp from 'node:child_process';import assert from 'node:assert/strict';
const base='64daf88',root=path.resolve('powerbi'),read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const old=JSON.parse(cp.execFileSync('git',['show',`${base}:powerbi/HUMAIN.SemanticModel/model.bim`],{encoding:'utf8',maxBuffer:8e6}));
const model=read(path.join(root,'HUMAIN.SemanticModel/model.bim')),tables=new Map(model.model.tables.map(t=>[t.name,t]));
const before=old.model.tables.find(t=>t.name==='Metrics').measures,now=tables.get('Metrics').measures;
for(const m of before){const n=now.find(n=>n.name===m.name);assert(n,m.name);assert.deepEqual(n.expression,m.expression,m.name+' semantics changed');const allowed=m.name==='Resolution Rate'?{...m,formatString:'0.0%'}:m;assert.deepEqual(n,allowed,m.name+' unexpected change');}
assert.deepEqual(model.model.relationships,old.model.relationships,'relationships changed');
for(const t of old.model.tables.filter(t=>!['Tests','Metrics'].includes(t.name)))assert.deepEqual(tables.get(t.name),t,'unexpected table change');
assert.deepEqual(tables.get('Tests').columns,old.model.tables.find(t=>t.name==='Tests').columns);
const walk=d=>fs.readdirSync(d,{withFileTypes:true}).flatMap(e=>e.isDirectory()?walk(path.join(d,e.name)):[path.join(d,e.name)]);
let parsed=0,bindings=0;
const fields=o=>{if(!o||typeof o!=='object')return;for(const kind of ['Column','Measure'])if(o[kind]?.Expression?.SourceRef?.Entity){const x=o[kind],t=tables.get(x.Expression.SourceRef.Entity);assert(t,JSON.stringify(x));assert((kind==='Column'?t.columns:t.measures)?.some(m=>m.name===x.Property),'Unknown '+x.Property);bindings++;}Object.values(o).forEach(fields);};
for(const f of walk(root))if(/\.(json|bim|pbir|pbism|platform)$/.test(f)){const x=read(f);parsed++;if(f.endsWith('visual.json'))fields(x);}
for(const r of ['HUMAIN.Final.Report','HUMAIN.Light.Report'])for(const page of ['ExecutiveOverview','ResolutionAudit']){
 const d=path.join(root,r,'definition/pages',page),p=read(path.join(d,'page.json')),files=walk(path.join(d,'visuals')).filter(f=>f.endsWith('visual.json')),vs=files.map(read),ids=new Set(vs.map(v=>v.name));
 for(const v of vs){const a=v.position;assert(a.x>=0&&a.y>=0&&a.x+a.width<=p.width&&a.y+a.height<=p.height,`Out of bounds ${r}/${page}/${v.name}`);}
 for(const i of p.visualInteractions??[])assert(ids.has(i.source)&&ids.has(i.target),'orphan interaction');
 for(let a=0;a<vs.length;a++)for(let b=a+1;b<vs.length;b++){const x=vs[a].position,y=vs[b].position;const overlap=Math.min(x.x+x.width,y.x+y.width)-Math.max(x.x,y.x)>1&&Math.min(x.y+x.height,y.y+y.height)-Math.max(x.y,y.y)>1;assert(!overlap,`Overlap ${page}: ${vs[a].name}/${vs[b].name}`);}
}
const canonical=o=>{if(Array.isArray(o))return o.map(canonical);if(o&&typeof o==='object')return Object.fromEntries(Object.keys(o).sort().filter(k=>k!=='tabOrder').map(k=>[k,canonical(o[k])]));if(typeof o==='string')return o.replace(/#[0-9a-f]{6}/ig,'#COLOR').replace(/(Color) Light/g,'$1');return o;};
const parity=[];
const changed=cp.execFileSync('git',['-c','core.safecrlf=false','diff','--name-only',base],{encoding:'utf8'}).trim().split('\n');
for(const page of ['ExecutiveOverview','ResolutionAudit','CriticalAging','TestPerformance','SnapshotQuality']){
 const dark=path.join(root,'HUMAIN.Final.Report/definition/pages',page),light=path.join(root,'HUMAIN.Light.Report/definition/pages',page);
 for(const f of walk(dark).filter(f=>f.endsWith('.json'))){const rel=path.relative(dark,f);if(JSON.stringify(canonical(read(f)))!==JSON.stringify(canonical(read(path.join(light,rel)))))parity.push(page+'/'+rel);}
}
assert(!changed.some(f=>f.includes('Clash3DExplorer')||f.includes('CustomVisuals')||f.startsWith('HUMAIN.CoordinationTracker/')),'protected paths changed');
console.log(JSON.stringify({parsed,bindings,existingMeasureExpressionsUnchanged:before.length,parityDifferences:parity},null,2));
if(parity.length)process.exitCode=1;
