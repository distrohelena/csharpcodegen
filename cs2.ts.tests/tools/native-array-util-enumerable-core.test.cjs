const assert = require('node:assert/strict');
const path = require('node:path');
const test = require('node:test');
const fs = require('node:fs');
const ts = require(path.resolve(__dirname, '../../cs2.ts/.net.ts/node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const { NativeArrayUtil } = require(path.join(root, 'system/util/nat-array-util.ts'));
const { StringComparer } = require(path.join(root, 'system/string-comparer.ts'));
const { ArgumentNullException } = require(path.join(root, 'system/argument-null.exception.ts'));

test('core Enumerable helpers preserve ordering and numeric aggregation', () => {
    assert.deepEqual(NativeArrayUtil.toArray(NativeArrayUtil.skip(['zero', 'one', 'two'], 1)), ['one', 'two']);
    assert.equal(NativeArrayUtil.all(['a', 'bb'], value => value.length > 0), true);
    assert.equal(NativeArrayUtil.sum([2, 3, 5], value => value), 10);
    assert.deepEqual(NativeArrayUtil.orderBy(['beta', 'Alpha', 'alpha'], value => value, StringComparer.OrdinalIgnoreCase), ['Alpha', 'alpha', 'beta']);
});

test('core Enumerable helpers reject null source and predicate like LINQ', () => {
    assert.throws(() => NativeArrayUtil.all(null, () => true), ArgumentNullException);
    assert.throws(() => NativeArrayUtil.all([], null), ArgumentNullException);
    assert.throws(() => NativeArrayUtil.sum(null, value => value), ArgumentNullException);
});
