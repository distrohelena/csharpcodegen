const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { Dictionary } = require(path.join(root, 'system/collections/generic/dictionary.ts'));

test('Dictionary Remove uses .NET key-removal semantics', () => {
    const values = new Dictionary();
    values.set('active', 7);
    assert.equal(values.Remove('active'), true);
    assert.equal(values.Count, 0);
    assert.equal(values.Remove('active'), false);
});
