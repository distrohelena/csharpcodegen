const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { NativeStringUtil } = require(path.join(root, 'system/util/nat-string-util.ts'));
const { ArgumentNullException } = require(path.join(root, 'system/argument-null.exception.ts'));

test('NativeStringUtil.join preserves .NET separator and null-element semantics', () => {
    assert.equal(NativeStringUtil.join(null, ['first', null, 'third']), 'firstthird');
    assert.equal(NativeStringUtil.join('/', 'left', null, 'right'), 'left//right');
    assert.equal(NativeStringUtil.join(',', new Set(['a', 'b'])), 'a,b');
});

test('NativeStringUtil.join rejects a null collection', () => {
    assert.throws(() => NativeStringUtil.join(',', null), ArgumentNullException);
});