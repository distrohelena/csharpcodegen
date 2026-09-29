const assert = require('node:assert/strict');
const { test } = require('node:test');
const path = require('node:path');
const fs = require('node:fs');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
const source = fs.readFileSync(path.join(root, 'system/unauthorized-access.exception.ts'), 'utf8');
const javascript = ts.transpileModule(source, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
} }).outputText;
const context = { exports: {}, Error, Object };
vm.runInNewContext(javascript, context);
const { UnauthorizedAccessException } = context.exports;

test('matches .NET default, null and explicit messages while retaining native Error identity', () => {
    for (const error of [new UnauthorizedAccessException(), new UnauthorizedAccessException(null)]) {
        assert.ok(error instanceof Error);
        assert.ok(error instanceof UnauthorizedAccessException);
        assert.equal(error.name, 'UnauthorizedAccessException');
        assert.equal(error.Message, 'Attempted to perform an unauthorized operation.');
        assert.equal(error.HResult, -2147024891);
        assert.equal(error.InnerException, null);
        assert.equal(typeof error.stack, 'string');
    }
    assert.equal(new UnauthorizedAccessException('').Message, '');
});

test('preserves the inner failure and exposes changes to the native message', () => {
    const inner = new Error('private inner detail');
    const error = new UnauthorizedAccessException('denied', inner);
    assert.equal(error.Message, 'denied');
    assert.equal(error.InnerException, inner);
    error.message = 'updated denial';
    assert.equal(error.Message, 'updated denial');
    assert.ok(!error.Message.includes('private inner detail'));
});
