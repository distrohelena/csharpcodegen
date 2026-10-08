const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { JsonSerializer, JsonException } = require(path.join(root, 'system/text/json/json-serializer.ts'));
const { JsonSerializerOptions } = require(path.join(root, 'system/text/json/json-serializer-options.ts'));
test('Deserialize accepts UTF8 byte arrays and sliced views without coercion', () => {
    const json = '{"value":"é😀","items":[1,true,null]}'; const encoded = new TextEncoder().encode(json);
    assert.deepEqual(JsonSerializer.Deserialize(encoded),JSON.parse(json));
    const padded = new Uint8Array(encoded.length+2); padded.set(encoded,1);
    assert.deepEqual(JsonSerializer.Deserialize(padded.subarray(1,padded.length-1)),JSON.parse(json));
});
test('Deserialize rejects empty JSON and malformed UTF8 rather than returning null or replacement text', () => {
    for (const input of ['',new Uint8Array(),new Uint8Array([0x22,0xff,0x22])]) assert.throws(() => JsonSerializer.Deserialize(input), JsonException);
    assert.equal(JsonSerializer.Deserialize('null'),null);
    assert.throws(() => JsonSerializer.Deserialize(null), error => error.ParamName === 'json');
});
test('trailing-comma option leaves comma-like string content untouched and rejects invalid empty items', () => {
    const options = new JsonSerializerOptions(); options.AllowTrailingCommas = true;
    assert.deepEqual(JsonSerializer.Deserialize('{"x":",]", "list":[1,],}',options),{x:',]',list:[1]});
    for(const input of ['[,]','[1,,]','{"x":,}']) assert.throws(() => JsonSerializer.Deserialize(input,options),JsonException);
});
