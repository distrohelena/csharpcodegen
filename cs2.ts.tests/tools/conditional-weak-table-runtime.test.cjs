const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { ConditionalWeakTable } = require(path.join(root, 'system/runtime/compiler-services/conditional-weak-table.ts'));

test('keys use object identity and a cached value is created once', () => {
    const table = new ConditionalWeakTable(); const first = { id: 1 }; const other = { id: 1 }; let calls = 0;
    const value = table.GetValue(first, key => { assert.equal(key, first); calls++; return []; });
    assert.equal(table.GetValue(first, () => { throw new Error('cached'); }), value);
    assert.notEqual(table.GetValue(other, () => []), value);
    assert.equal(calls, 1); assert.equal(table.Count(), 2);
});
test('missing out value is null and null values remain present', () => {
    const table = new ConditionalWeakTable(); const key = {}; const out = { value: 'stale' };
    assert.equal(table.TryGetValue(key, out), false); assert.equal(out.value, null);
    table.Add(key, null); assert.equal(table.TryGetValue(key, out), true); assert.equal(out.value, null);
    assert.throws(() => table.Add(key, {})); assert.equal(table.Remove(key), true); assert.equal(table.Remove(key), false);
});
test('factory failures are not cached and reentrant insertion wins', () => {
    const table = new ConditionalWeakTable(); const key = {}; const inner = {};
    assert.throws(() => table.GetValue(key, () => { throw new Error('factory'); }), /factory/);
    assert.equal(table.Count(), 0);
    assert.equal(table.GetValue(key, () => { table.Add(key, inner); return {}; }), inner);
    assert.equal(table.Count(), 1);
});
test('clear removes mappings and enumeration yields key value pairs', () => {
    const table = new ConditionalWeakTable(); const key = {}; const value = {};
    table.Add(key, value); const entries = [...table];
    assert.equal(entries.length, 1); assert.equal(entries[0].Key, key); assert.equal(entries[0].Value, value);
    table.Clear(); assert.equal(table.Count(), 0); assert.equal(table.TryGetValue(key, { value }), false);
    table.Add(key, value); assert.equal(table.Count(), 1);
});
test('invalid keys and factory are rejected before changing the table', () => {
    const table = new ConditionalWeakTable();
    for (const key of [null, undefined, 1, 'key', Symbol('key')]) {
        assert.throws(() => table.Add(key, {})); assert.throws(() => table.Remove(key));
        assert.throws(() => table.TryGetValue(key, { value: null })); assert.throws(() => table.GetValue(key, () => ({})));
    }
    const key = {}; table.Add(key, {}); assert.throws(() => table.GetValue(key, null)); assert.equal(table.Count(), 1);
});
test('value back-reference does not keep an abandoned transaction alive', { skip: typeof global.gc !== 'function' }, async () => {
    const table = new ConditionalWeakTable(); let key = {}; table.Add(key, { transaction: key });
    const probe = new WeakRef(key); key = null;
    for (let attempt = 0; attempt < 40; attempt++) {
        await new Promise(resolve => setImmediate(resolve)); global.gc();
        if (probe.deref() === undefined) { assert.equal(table.Count(), 0); return; }
    }
    assert.fail('The weak table retained the abandoned transaction and its value cycle.');
});
