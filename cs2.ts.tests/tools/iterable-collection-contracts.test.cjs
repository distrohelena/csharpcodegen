const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { NativeArrayUtil } = require(path.join(root, 'system/util/nat-array-util.ts'));
const { StringComparer } = require(path.join(root, 'system/string-comparer.ts'));
const { HashSet } = require(path.join(root, 'system/collections/generic/hash-set.ts'));
const { List } = require(path.join(root, 'system/collections/generic/list.ts'));
const { ConcurrentDictionary } = require(path.join(root, 'system/collections/concurrent/concurrent-dictionary.ts'));
test('Take is deferred, consumes only requested elements, closes enumerator and can be enumerated again', () => {
    let reads=0, closes=0;
    const source={ *[Symbol.iterator]() { try { for(let i=0;i<10;i++) { reads++; yield i; } } finally { closes++; } } };
    const taken=NativeArrayUtil.take(source,2); assert.equal(reads,0);
    assert.deepEqual([...taken],[0,1]); assert.equal(reads,2); assert.equal(closes,1);
    assert.deepEqual([...taken],[0,1]); assert.equal(reads,4);
    assert.deepEqual([...NativeArrayUtil.take(source,0)],[]); assert.equal(reads,4);
});
test('Distinct preserves first values, is deferred and honors equality comparer including null and NaN', () => {
    const source=['Alpha','alpha','Beta']; const distinct=NativeArrayUtil.distinct(source,StringComparer.OrdinalIgnoreCase);
    source.push('gamma'); assert.deepEqual([...distinct],['Alpha','Beta','gamma']);
    assert.deepEqual([...NativeArrayUtil.distinct([NaN,NaN,null,null,1])],[NaN,null,1]);
});
test('Contains short circuits iterables and distinguishes omitted comparer from explicit default comparer', () => {
    const source={ *[Symbol.iterator]() { yield 'Alpha'; throw new Error('over-read'); } };
    assert.equal(NativeArrayUtil.contains(source,'ALPHA',StringComparer.OrdinalIgnoreCase),true);
    const set=new HashSet(StringComparer.OrdinalIgnoreCase); set.Add('Alpha');
    assert.equal(NativeArrayUtil.contains(set,'ALPHA'),true);
    assert.equal(NativeArrayUtil.contains(set,'ALPHA',null),false);
    assert.equal(NativeArrayUtil.contains([NaN],NaN),true);
});
test('ToList materializes an independent mutable List from any iterable and null inputs fail eagerly', () => {
    const source=new Set([1,2]); const list=NativeArrayUtil.toList(source);
    assert.ok(list instanceof List); list.add(3); assert.equal(source.size,2); assert.deepEqual([...list],[1,2,3]);
    for(const run of [()=>NativeArrayUtil.take(null,1),()=>NativeArrayUtil.distinct(null),()=>NativeArrayUtil.contains(null,1),()=>NativeArrayUtil.toList(null)]) assert.throws(run);
});
test('ConcurrentDictionary Remove conforms to IDictionary and preserves counts for missing keys', () => {
    const map=new ConcurrentDictionary(); map.add('first',1); map.add('second',2);
    assert.equal(map.Remove('first'),true); assert.equal(map.Count,1); assert.equal(map.Remove('missing'),false); assert.equal(map.Count,1);
    assert.deepEqual(map.ToArray(),[{Key:'second',Value:2}]);
});
