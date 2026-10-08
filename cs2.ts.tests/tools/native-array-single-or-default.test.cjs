const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);

const { NativeArrayUtil } = require(path.join(root, 'system/util/nat-array-util.ts'));
const { ArgumentNullException } = require(path.join(root, 'system/argument-null.exception.ts'));
const { InvalidOperationException } = require(path.join(root, 'system/invalid-operation.exception.ts'));

test('NativeArrayUtil.singleOrDefault returns one match or null and rejects duplicate matches', () => {
    assert.equal(NativeArrayUtil.singleOrDefault(['alpha', 'beta'], value => value === 'beta'), 'beta');
    assert.equal(NativeArrayUtil.singleOrDefault(['alpha', 'beta'], value => value === 'none'), null);
    assert.throws(() => NativeArrayUtil.singleOrDefault(['alpha', 'alpha'], value => value === 'alpha'), InvalidOperationException);
});

test('NativeArrayUtil.singleOrDefault preserves Enumerable null argument semantics', () => {
    assert.throws(() => NativeArrayUtil.singleOrDefault(null, () => false), ArgumentNullException);
    assert.throws(() => NativeArrayUtil.singleOrDefault(['alpha'], null), ArgumentNullException);
});

test('NativeArrayUtil.sequenceEqual uses its comparer and preserves Enumerable null argument semantics', () => {
    const ordinalIgnoreCase = { Equals: (left, right) => left.toLowerCase() === right.toLowerCase() };
    assert.equal(NativeArrayUtil.sequenceEqual(['Alpha', 'Beta'], ['alpha', 'beta'], ordinalIgnoreCase), true);
    assert.equal(NativeArrayUtil.sequenceEqual(['Alpha'], ['alpha', 'beta'], ordinalIgnoreCase), false);
    assert.throws(() => NativeArrayUtil.sequenceEqual(null, [], ordinalIgnoreCase), ArgumentNullException);
    assert.throws(() => NativeArrayUtil.sequenceEqual([], null, ordinalIgnoreCase), ArgumentNullException);
    assert.throws(() => NativeArrayUtil.sequenceEqual([], [], null), ArgumentNullException);
});