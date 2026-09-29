const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
const previousLoader = require.extensions['.ts'];
require.extensions['.ts'] = (module, filename) => {
    const source = fs.readFileSync(filename, 'utf8');
    module._compile(ts.transpileModule(source, { compilerOptions: {
        module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
    } }).outputText, filename);
};
const { JsonElement } = require(path.join(root, 'system/text/json/json-element.ts'));
const { JsonValueKind } = require(path.join(root, 'system/text/json/json-value-kind.ts'));
const { KeyNotFoundException } = require(path.join(root, 'system/collections/generic/key-not-found.exception.ts'));
const { InvalidOperationException } = require(path.join(root, 'system/invalid-operation.exception.ts'));
const { ArgumentNullException } = require(path.join(root, 'system/argument-null.exception.ts'));
if (previousLoader) require.extensions['.ts'] = previousLoader;
else delete require.extensions['.ts'];

test('requires own case-sensitive properties, including empty and prototype-like names', () => {
    const value = new JsonElement(JSON.parse('{"":1,"A":null,"a":false,"__proto__":2,"constructor":3}'));
    assert.equal(value.GetProperty('').GetDecimal(), 1);
    assert.equal(value.GetProperty('A').ValueKind, JsonValueKind.Null);
    assert.equal(value.GetProperty('a').GetBoolean(), false);
    assert.equal(value.GetProperty('__proto__').GetDecimal(), 2);
    assert.equal(value.GetProperty('constructor').GetDecimal(), 3);
    assert.throws(() => value.GetProperty('toString'), KeyNotFoundException);
    assert.throws(() => value.GetProperty('missing'), KeyNotFoundException);
    const result = { value: value.GetProperty('A') };
    assert.equal(value.TryGetProperty('missing', result), false);
    assert.equal(result.value.ValueKind, JsonValueKind.Undefined);
    assert.equal(value.TryGetProperty('a', result), true);
    assert.equal(result.value.GetBoolean(), false);
});

test('uses the last duplicate property when given parsed JSON', () => {
    const value = new JsonElement(JSON.parse('{"a":true,"a":false}'));
    assert.equal(value.GetProperty('a').GetBoolean(), false);
});

for (const value of [undefined, null, true, false, 42, 'true', []]) {
    test('rejects object lookup and enumeration on incompatible kind: ' + String(value), () => {
        const element = new JsonElement(value);
        assert.throws(() => element.GetProperty('x'), InvalidOperationException);
        assert.throws(() => element.TryGetProperty('x', { value: null }), InvalidOperationException);
        assert.throws(() => element.EnumerateObject(), InvalidOperationException);
        assert.throws(() => element.GetProperty(null), ArgumentNullException);
        assert.throws(() => element.TryGetProperty(null, { value: null }), ArgumentNullException);
    });
}

test('does not coerce scalar kinds into strings or booleans', () => {
    assert.equal(new JsonElement('true').GetString(), 'true');
    assert.equal(new JsonElement(null).GetString(), null);
    assert.equal(new JsonElement(true).GetBoolean(), true);
    assert.equal(new JsonElement(false).GetBoolean(), false);
    for (const value of [undefined, null, 0, 1, 'true', 'false', [], {}]) {
        assert.throws(() => new JsonElement(value).GetBoolean(), InvalidOperationException);
    }
    for (const value of [undefined, 0, true, false, [], {}]) {
        assert.throws(() => new JsonElement(value).GetString(), InvalidOperationException);
    }
});

test('requires an array for both length and enumeration, retaining null entries', () => {
    const value = new JsonElement([1, null, false]);
    assert.equal(value.GetArrayLength(), 3);
    assert.deepEqual(Array.from(value.EnumerateArray()).map(item => item.ValueKind), [JsonValueKind.Number, JsonValueKind.Null, JsonValueKind.False]);
    for (const item of [undefined, null, false, 1, '[]', {}]) {
        assert.throws(() => new JsonElement(item).GetArrayLength(), InvalidOperationException);
        assert.throws(() => new JsonElement(item).EnumerateArray(), InvalidOperationException);
    }
});

test('matches .NET missing-key exception metadata and native Error identity', () => {
    const inner = new Error('inner');
    for (const error of [new KeyNotFoundException(), new KeyNotFoundException(null)]) {
        assert.ok(error instanceof Error);
        assert.equal(error.Message, 'The given key was not present in the dictionary.');
        assert.equal(error.HResult, -2146232969);
        assert.equal(error.name, 'KeyNotFoundException');
        assert.equal(error.InnerException, null);
    }
    const error = new KeyNotFoundException('', inner);
    assert.equal(error.Message, '');
    assert.equal(error.InnerException, inner);
});
