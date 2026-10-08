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
const { StringSplitOptions } = require(path.join(root, 'system/string-split-options.ts'));
const { ArgumentNullException } = require(path.join(root, 'system/argument-null.exception.ts'));

test('NativeStringUtil.split handles RemoveEmptyEntries and TrimEntries', () => {
    assert.deepEqual(NativeStringUtil.split('/alpha//beta/', '/', StringSplitOptions.RemoveEmptyEntries), ['alpha', 'beta']);
    assert.deepEqual(NativeStringUtil.split(' first | | second ', '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ['first', 'second']);
    assert.deepEqual(NativeStringUtil.split('a,,b', ',', StringSplitOptions.None), ['a', '', 'b']);
});

test('NativeStringUtil.split rejects null source and separator', () => {
    assert.throws(() => NativeStringUtil.split(null, '/', StringSplitOptions.None), ArgumentNullException);
    assert.throws(() => NativeStringUtil.split('a', null, StringSplitOptions.None), ArgumentNullException);
});