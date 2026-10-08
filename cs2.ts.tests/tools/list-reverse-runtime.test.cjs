const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { List } = require(path.join(root, 'system/collections/generic/list.ts'));
test('List.Reverse mutates the sequence and returns void', () => {
    const values = new List(['first', 'second', 'third']);
    assert.equal(values.Reverse(), undefined);
    assert.deepEqual(Array.from(values), ['third', 'second', 'first']);
});
