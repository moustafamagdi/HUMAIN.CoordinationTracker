import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const read = p => JSON.parse(fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, ''));
const model = read('powerbi/HUMAIN.SemanticModel/model.bim');
const tables = new Map(model.model.tables.map(t => [t.name, t]));
const normalize = value => {
  if (typeof value === 'string') return value.replace(/#[0-9a-f]{6}/ig, '#COLOR').replace(/ Color Light/g, ' Color');
  if (Array.isArray(value)) return value.map(normalize);
  if (value && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map(k => [k, normalize(value[k])]));
  return value;
};
let bindings = 0;
function checkFields(o) {
  if (!o || typeof o !== 'object') return;
  for (const kind of ['Measure', 'Column']) {
    const field = o[kind];
    if (!field?.Expression?.SourceRef?.Entity) continue;
    const table = tables.get(field.Expression.SourceRef.Entity);
    assert(table, 'Unknown table');
    assert((kind === 'Measure' ? table.measures : table.columns).some(f => f.name === field.Property), field.Property);
    bindings++;
  }
  Object.values(o).forEach(checkFields);
}
const reports = ['HUMAIN.Final.Report', 'HUMAIN.Light.Report'];
const definitions = reports.map(report => {
  const root = path.join('powerbi', report, 'definition/pages');
  const meta = read(path.join(root, 'pages.json'));
  assert.equal(meta.pageOrder.filter(n => n === 'TopLevel').length, 1);
  const page = read(path.join(root, 'TopLevel/page.json'));
  const folder = path.join(root, 'TopLevel/visuals');
  const visuals = fs.readdirSync(folder).sort().map(n => read(path.join(folder, n, 'visual.json')));
  assert.equal(visuals.length, 24);
  const names = new Set(visuals.map(v => v.name));
  for (const interaction of page.visualInteractions) {
    assert(names.has(interaction.source) && names.has(interaction.target));
    assert.equal(interaction.type, 'NoFilter');
  }
  for (const v of visuals) {
    checkFields(v);
    const a = v.position;
    assert(a.x >= 0 && a.y >= 0 && a.x + a.width <= page.width && a.y + a.height <= page.height, v.name);
    for (const u of visuals) {
      if (u === v) continue;
      const b = u.position;
      assert(!(Math.min(a.x + a.width, b.x + b.width) > Math.max(a.x, b.x) && Math.min(a.y + a.height, b.y + b.height) > Math.max(a.y, b.y)), `${v.name} overlaps ${u.name}`);
    }
  }
  return { page, visuals };
});
assert.deepEqual(normalize(definitions[0]), normalize(definitions[1]), 'Dark/Light parity');
assert.equal(tables.get('Metrics').measures.filter(m => m.name.startsWith('Top Level ')).length, 23);
console.log(JSON.stringify({visuals: 48, bindings, measures: 23, bounds: 'passed', overlap: 'none', parity: 'passed'}, null, 2));
