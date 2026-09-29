const assert = require('node:assert/strict');
const { test } = require('node:test');
const path = require('node:path');
const fs = require('node:fs');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
const source = fs.readFileSync(path.join(root, 'system/format.exception.ts'), 'utf8');
const javascript = ts.transpileModule(source, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
} }).outputText;
const context = { exports: {}, Error, Object };
vm.runInNewContext(javascript, context);
const { FormatException } = context.exports;

test('preserves the standard invalid-format message and native Error identity', () => {
    for (const error of [new FormatException(), new FormatException(null)]) {
        assert.ok(error instanceof Error);
        assert.ok(error instanceof FormatException);
        assert.equal(error.name, 'FormatException');
        assert.equal(error.message, 'One of the identified items was in an invalid format.');
        assert.equal(typeof error.stack, 'string');
        assert.equal(error.InnerException, undefined);
    }
    assert.equal(new FormatException('').message, '');
});

test('preserves an explicit inner error independently from the public message', () => {
    const inner = new Error('inner conversion failure');
    const error = new FormatException('invalid input', inner);
    assert.equal(error.message, 'invalid input');
    assert.equal(error.InnerException, inner);
    assert.ok(!error.message.includes(inner.message));
});
