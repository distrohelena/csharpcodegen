'use strict';
const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { JsonNode } = require(path.join(root, 'system/text/json/nodes/json-node.ts'));
const { JsonObject } = require(path.join(root, 'system/text/json/nodes/json-object.ts'));
const { JsonArray } = require(path.join(root, 'system/text/json/nodes/json-array.ts'));
const { JsonValue } = require(path.join(root, 'system/text/json/nodes/json-value.ts'));

test('JsonNode Parse preserves object keys, array order and scalar node identities', () => {
    const rootNode = JsonNode.Parse('{"limit":4,"names":["alpha","beta"],"nested":{"enabled":true}}');
    assert.ok(rootNode instanceof JsonObject);
    assert.equal(rootNode.Count, 3);
    assert.deepEqual([...rootNode].map(pair => pair.Key), ['limit', 'names', 'nested']);

    const value = { value: undefined };
    assert.equal(rootNode.TryGetPropertyValue('limit', value), true);
    assert.ok(value.value instanceof JsonValue);
    const number = { value: undefined };
    assert.equal(value.value.TryGetValue(number), true);
    assert.equal(number.value, 4);

    const names = { value: undefined };
    assert.equal(rootNode.TryGetPropertyValue('names', names), true);
    assert.ok(names.value instanceof JsonArray);
    assert.equal(names.value.Count, 2);
    assert.equal(names.value[0].ToJsonString(), '"alpha"');
    assert.deepEqual([...names.value].map(node => node.ToJsonString()), ['"alpha"', '"beta"']);
    assert.equal(rootNode.ToJsonString(), '{"limit":4,"names":["alpha","beta"],"nested":{"enabled":true}}');
});

test('Json Nodes allow index writes and reject missing/non-numeric reads explicitly', () => {
    const rootNode = JsonNode.Parse('{"numbers":[1,2]}');
    const numbers = { value: undefined };
    assert.equal(rootNode.TryGetPropertyValue('numbers', numbers), true);
    numbers.value[1] = JsonNode.Parse('8');
    assert.equal(numbers.value.ToJsonString(), '[1,8]');

    const missing = { value: 'sentinel' };
    assert.equal(rootNode.TryGetPropertyValue('missing', missing), false);
    assert.equal(missing.value, null);
    const text = JsonNode.Parse('"four"');
    const result = { value: 123 };
    assert.equal(text.TryGetValue(result), false);
    assert.equal(result.value, null);
    assert.throws(() => JsonNode.Parse('{invalid'), SyntaxError);
});