const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { JsonDocument } = require(path.join(root, 'system/text/json/json-document.ts'));
const { JsonElement } = require(path.join(root, 'system/text/json/json-element.ts'));
const { Encoding } = require(path.join(root, 'system/text/encoding.ts'));

test('JSON ToString matches CLR kinds and preserves original numeric and container text', () => {
    for (const [json, expected] of [['null',''], ['true','True'], ['false','False'], ['"a\\nb"','a\nb'], ['1e+03','1e+03'], ['{ "x" : 1.0 }','{ "x" : 1.0 }'], ['[ 1, 2 ]','[ 1, 2 ]']]) {
        assert.equal(JsonDocument.Parse(json).RootElement.ToString(), expected);
    }
    assert.equal(new JsonElement(undefined).ToString(), '');
});
test('integer getters reject fractional and exponent tokens and enforce signed bounds', () => {
    for (const [json, ok, expected] of [['-2147483648',true,-2147483648], ['2147483647',true,2147483647], ['2147483648',false,0], ['-2147483649',false,0], ['1.0',false,0], ['1e0',false,0], ['-0',true,0]]) {
        const out = { value: 99 }; assert.equal(JsonDocument.Parse(json).RootElement.TryGetInt32(out), ok, json); assert.equal(out.value, expected, json);
    }
    for (const json of ['"1"','true','null','[]','{}']) assert.throws(() => JsonDocument.Parse(json).RootElement.TryGetInt32({ value: 0 }));
    for (const json of ['1.0','1e0','9223372036854775808','-9223372036854775809']) {
        const out = { value: 99 }; assert.equal(JsonDocument.Parse(json).RootElement.TryGetInt64(out), false, json); assert.equal(out.value,0);
    }
});
test('nested properties and array enumerators retain lexical numeric tokens and duplicate property semantics', () => {
    const element = JsonDocument.Parse('{ "x": 1, "x": 1e0, "__proto__": 2.0, "list": [1.0, {"n": 4e0}] }').RootElement;
    assert.equal(element.GetProperty('x').GetRawText(), '1e0');
    assert.equal(element.GetProperty('__proto__').TryGetInt32({ value: 0 }), false);
    const entries = [...element.GetProperty('list').EnumerateArray()];
    assert.equal(entries[0].GetRawText(), '1.0'); assert.equal(entries[1].GetProperty('n').GetRawText(), '4e0');
    const properties = [...element.EnumerateObject()]; assert.equal(properties.find(p => p.Name === 'x').Value.GetRawText(), '1e0');
});
test('UTF8 byte documents and byte counts handle nonASCII and malformed UTF16 consistently', () => {
    const text = '{"word":"é😀"}'; assert.equal(JsonDocument.Parse(new TextEncoder().encode(text)).RootElement.GetProperty('word').GetString(),'é😀');
    for (const text of ['', 'abc', 'é😀', '\ud800']) assert.equal(Encoding.UTF8.GetByteCount(text), new TextEncoder().encode(text).length);
    assert.equal(Encoding.ASCII.GetByteCount('é😀'),3); assert.throws(() => Encoding.UTF8.GetByteCount(null));
    assert.throws(() => JsonDocument.Parse(new Uint8Array([0x22,0xff,0x22])));
});

test('numeric and display contracts match an actual CLR fixture', { skip: !process.env.SSN_JSON_PARITY_FIXTURE }, () => {
    for (const row of JSON.parse(fs.readFileSync(process.env.SSN_JSON_PARITY_FIXTURE,'utf8'))) {
        const element = JsonDocument.Parse(row.Input).RootElement;
        assert.equal(element.ToString(), row.Display, row.Input); assert.equal(element.GetRawText(), row.Raw, row.Input);
        for (const bits of [32,64]) {
            const out = { value: 99 }; const ok = row[`Int${bits}Ok`];
            if (ok === null) assert.throws(() => element[`TryGetInt${bits}`](out), row.Input);
            else {
                assert.equal(element[`TryGetInt${bits}`](out),ok,row.Input);
                assert.equal(out.value,Number(row[`Int${bits}`]),row.Input);
            }
        }
    }
});
