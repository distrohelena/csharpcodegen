const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
function importPath(file) {
    return path.join(root, file).replace(/\\/g, '/');
}
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);

const { JavaScriptEncoder } = require(path.join(root, 'system/text/encodings/web/javascript-encoder.ts'));
const { JsonWriterOptions } = require(path.join(root, 'system/text/json/json-writer-options.ts'));
const { Utf8JsonWriter } = require(path.join(root, 'system/text/json/utf8-json-writer.ts'));
const { JsonDocument } = require(path.join(root, 'system/text/json/json-document.ts'));
const { JsonSerializer } = require(path.join(root, 'system/text/json/json-serializer.ts'));
const { JsonSerializerOptions } = require(path.join(root, 'system/text/json/json-serializer-options.ts'));
const { Type } = require(path.join(root, 'src/reflection.ts'));
const { JsonNode } = require(path.join(root, 'system/text/json/nodes/json-node.ts'));

test('UnsafeRelaxedJsonEscaping keeps browser JSON writer payload characters unescaped', () => {
    const writer = new Utf8JsonWriter(undefined, Object.assign(new JsonWriterOptions(), {
        Encoder: JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }));
    writer.WriteStartObject();
    writer.WriteString('value', '<>&+');
    writer.WriteEndObject();

    assert.equal(writer.toString(), '{"value":"<>&+"}');
});

test('Utf8JsonWriter writes base64 and validated raw JSON values', () => {
    const writer = new Utf8JsonWriter();
    writer.WriteStartObject();
    writer.WriteBase64String('bytes', new Uint8Array([0, 255, 1]));
    writer.WritePropertyName('nested');
    writer.WriteRawValue('{"z":2}', false);
    writer.WriteEndObject();

    assert.equal(writer.toString(), '{"bytes":"AP8B","nested":{"z":2}}');
    assert.throws(() => new Utf8JsonWriter().WriteRawValue('{invalid}', false));
});

test('JsonElement writes its original JSON value through Utf8JsonWriter', () => {
    const document = JsonDocument.Parse('{"items":[1,true]}');
    const writer = new Utf8JsonWriter();
    document.RootElement.WriteTo(writer);

    assert.equal(writer.toString(), '{"items":[1,true]}');
});

test('JsonSerializer supports the C# object-and-Type Serialize overload', () => {
    const fixture = path.join(require('node:os').tmpdir(), `cs2-json-serialize-${process.pid}.ts`);
    const source = [
        `import { JsonSerializer } from '${importPath('system/text/json/json-serializer')}';`,
        `import { JsonSerializerOptions } from '${importPath('system/text/json/json-serializer-options')}';`,
        `import { JsonNode, JsonValue } from '${importPath('system/text/json/nodes/json-node')}';`,
        `import { Type } from '${importPath('src/reflection')}';`,
        'const payload = { id: "record" };',
        'const serialized: string = JsonSerializer.Serialize(payload, Type.of(payload), new JsonSerializerOptions());',
        'const numericNode = JsonNode.FromValue(7);',
        'const numericOut: { value?: number } = {};',
        'if (numericNode instanceof JsonValue) numericNode.TryGetValue<number>(numericOut);',
        'serialized;'
    ].join('\n');
    fs.writeFileSync(fixture, source);
    try {
        const program = ts.createProgram([fixture], {
            noEmit: true,
            strict: true,
            target: ts.ScriptTarget.ES2020,
            module: ts.ModuleKind.CommonJS,
            skipLibCheck: true
        });
        const diagnostics = ts.getPreEmitDiagnostics(program)
            .filter(diagnostic => diagnostic.category === ts.DiagnosticCategory.Error);
        assert.equal(diagnostics.length, 0, diagnostics.map(diagnostic => ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')).join('\n'));
    } finally {
        fs.rmSync(fixture, { force: true });
    }

    const payload = { id: 'record' };
    assert.equal(
        JsonSerializer.Serialize(payload, Type.of(payload), new JsonSerializerOptions()),
        '{"id":"record"}');
    const numberOut = { value: undefined };
    assert.equal(JsonNode.FromValue(7).TryGetValue(numberOut), true);
    assert.equal(numberOut.value, 7);
});
