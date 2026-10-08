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

test('NativeStringUtil.any uses .NET string UTF-16 char iteration and short-circuits', () => {
    const seen = [];
    const found = NativeStringUtil.any('a😀b', value => {
        seen.push(value);
        return value === 'b';
    });
    assert.equal(found, true);
    assert.equal(seen.length, 4);
    assert.equal(seen[1].length, 1);
    assert.equal(seen[2].length, 1);
    assert.equal(NativeStringUtil.any('abc', value => value === 'z'), false);
});

test('NativeStringUtil.any preserves Enumerable null argument semantics', () => {
    assert.throws(() => NativeStringUtil.any(null, () => false), ArgumentNullException);
    assert.throws(() => NativeStringUtil.any('a', null), ArgumentNullException);
});

test('NativeStringUtil.all uses .NET string UTF-16 char iteration and short-circuits failures', () => {
    const visited = [];
    assert.equal(NativeStringUtil.all('A😀', character => { visited.push(character); return character.charCodeAt(0) !== 0xd83d; }), false);
    assert.deepEqual(visited, ['A', String.fromCharCode(0xd83d)]);
    assert.equal(NativeStringUtil.all('', () => false), true);
    assert.throws(() => NativeStringUtil.all(null, () => true), ArgumentNullException);
    assert.throws(() => NativeStringUtil.all('a', null), ArgumentNullException);
});
