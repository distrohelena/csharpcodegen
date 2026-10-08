const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { NativeStringUtil } = require(path.join(root, 'system/util/nat-string-util.ts'));
const { ArgumentNullException } = require(path.join(root, 'system/argument-null.exception.ts'));
const { ArgumentException } = require(path.join(root, 'system/argument.exception.ts'));

test('NativeStringUtil replace mirrors .NET literal all-occurrence replacement', () => {
    assert.equal(NativeStringUtil.replace('a.a.a', '.', '/'), 'a/a/a');
    assert.equal(NativeStringUtil.replace('a$a$', 'a$', '?'), '??');
    assert.equal(NativeStringUtil.replace('a--a', '--', null), 'aa');
});

test('NativeStringUtil replace preserves .NET argument failures', () => {
    assert.throws(() => NativeStringUtil.replace('value', null, 'x'), ArgumentNullException);
    assert.throws(() => NativeStringUtil.replace('value', '', 'x'), ArgumentException);
});