import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
const root=path.resolve(process.argv[2] || 'powerbi');
const old='this.zoomObjects=e=>{this.renderer&&(0,o.d6)(this.renderer,this.bridge.getModelMaps,this.toGroups(e))}';
const replacement='this.zoomObjects=e=>{const r=this.renderer;if(!r)return;const n=this.humainZoomRevision=(this.humainZoomRevision||0)+1,t=this.toGroups(e);r.requestRender();requestAnimationFrame(()=>requestAnimationFrame(()=>{if(this.renderer!==r||this.humainZoomRevision!==n)return;setTimeout(()=>{if(this.renderer!==r||this.humainZoomRevision!==n)return;(0,o.d6)(r,this.bridge.getModelMaps,t);r.requestRender()},120)}))}';
for(const report of ['HUMAIN.Light.Report','HUMAIN.Final.Report']) {
 const file=path.join(root,report,'CustomVisuals/specklePowerBiVisual/resources/specklePowerBiVisual.pbiviz.json');
 const raw=fs.readFileSync(file,'utf8'),p=JSON.parse(raw);
 if(p.content.js.includes('humainZoomRevision'))throw Error('Already patched');
 if(p.content.js.split(old).length!==2)throw Error('Unexpected visual version');
 fs.mkdirSync('work/speckle-camera-backup',{recursive:true});
 fs.writeFileSync(`work/speckle-camera-backup/${report}.pbiviz.json`,raw);
 p.content.js=p.content.js.replace(old,replacement);
 new vm.Script(p.content.js);
 fs.writeFileSync(file,JSON.stringify(p));
}
console.log('Patched deferred camera fit in both local report resources; originals backed up.');

