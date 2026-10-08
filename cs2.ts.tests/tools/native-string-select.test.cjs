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

test('NativeStringUtil.select preserves UTF-16 char order and creates a detached char array', () => {
    const selected = NativeStringUtil.select('a😀b', (character, index) => `${index}:${character.charCodeAt(0).toString(16)}`);
    assert.deepEqual(selected, ['0:61', '1:d83d', '2:de00', '3:62']);
    selected[0] = 'changed';
    assert.equal('a😀b'.charAt(0), 'a');
});

test('NativeStringUtil.select follows Enumerable null argument semantics', () => {
    assert.throws(() => NativeStringUtil.select(null, () => 'x'), ArgumentNullException);
    assert.throws(() => NativeStringUtil.select('a', null), ArgumentNullException);
});