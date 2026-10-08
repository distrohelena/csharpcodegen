const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));

require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);

const { Convert } = require(path.join(root, 'system/convert.ts'));

test('Convert.ToString preserves null, primitive and invariant-provider conversions', () => {
    const invariantProvider = {};
    assert.equal(Convert.ToString(null, invariantProvider), '');
    assert.equal(Convert.ToString(undefined, invariantProvider), '');
    assert.equal(Convert.ToString('domain-a', invariantProvider), 'domain-a');
    assert.equal(Convert.ToString(true, invariantProvider), 'True');
    assert.equal(Convert.ToString(false, invariantProvider), 'False');
    assert.equal(Convert.ToString(42, invariantProvider), '42');
    assert.equal(Convert.ToString(12.5, invariantProvider), '12.5');
});

test('Convert.ToInt64 rounds numeric values to even and parses integral strings', () => {
    const invariantProvider = {};
    assert.equal(Convert.ToInt64(null, invariantProvider), 0);
    assert.equal(Convert.ToInt64(false, invariantProvider), 0);
    assert.equal(Convert.ToInt64(true, invariantProvider), 1);
    assert.equal(Convert.ToInt64(1.5, invariantProvider), 2);
    assert.equal(Convert.ToInt64(2.5, invariantProvider), 2);
    assert.equal(Convert.ToInt64(-1.5, invariantProvider), -2);
    assert.equal(Convert.ToInt64(' -42 ', invariantProvider), -42);
    assert.equal(Convert.ToInt64('+17', invariantProvider), 17);
});

test('Convert.ToInt64 rejects malformed, non-finite and out-of-range inputs', () => {
    const invariantProvider = {};
    assert.equal(typeof Convert.ToInt64, 'function');
    assert.throws(() => Convert.ToInt64('', invariantProvider));
    assert.throws(() => Convert.ToInt64('12.5', invariantProvider));
    assert.throws(() => Convert.ToInt64(Number.NaN, invariantProvider));
    assert.throws(() => Convert.ToInt64(Number.POSITIVE_INFINITY, invariantProvider));
    assert.throws(() => Convert.ToInt64('9223372036854775808', invariantProvider));
});
