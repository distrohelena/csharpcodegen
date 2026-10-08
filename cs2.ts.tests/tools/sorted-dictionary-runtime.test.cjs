const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);

const { SortedDictionary } = require(path.join(root, 'system/collections/generic/sorted-dictionary.ts'));
const { StringComparer } = require(path.join(root, 'system/string-comparer.ts'));

test('SortedDictionary New2 serializes canonical string keys in comparer order', () => {
    const values = SortedDictionary.New2(StringComparer.Ordinal);
    values.set('zeta', 'last');
    values.set('alpha', 'first');
    values.set('middle', 'center');
    values.set('alpha', 'replaced');

    assert.deepEqual([...values].map(entry => [entry.Key, entry.Value]), [
        ['alpha', 'replaced'],
        ['middle', 'center'],
        ['zeta', 'last']
    ]);
    assert.equal(JSON.stringify(values), '{"alpha":"replaced","middle":"center","zeta":"last"}');
});
