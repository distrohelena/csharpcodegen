const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { ConcurrentDictionary } = require(path.join(root, 'system/collections/concurrent/concurrent-dictionary.ts'));

test('ConcurrentDictionary supports replay-cache snapshot and removal semantics', () => {
    const values = new ConcurrentDictionary();
    assert.equal(values.TryAdd('first', 1), true);
    assert.equal(values.TryAdd('first', 2), false);
    values.set('first', 3);
    assert.equal(values.GetValueOrDefault('first', 0), 3);
    assert.equal(values.GetValueOrDefault('missing', 9), 9);
    assert.deepEqual(values.ToArray(), [{ Key: 'first', Value: 3 }]);
    const removed = { value: undefined };
    assert.equal(values.TryRemove('first', removed), true);
    assert.equal(removed.value, 3);
    assert.equal(values.TryRemove('first', removed), false);
    assert.equal(removed.value, undefined);
});
