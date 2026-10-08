const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));

require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);

const { InvalidOperationException } = require(path.join(root, 'system/invalid-operation.exception.ts'));

test('InvalidOperationException retains a supplied inner exception', () => {
    const cause = new Error('original failure');
    const error = new InvalidOperationException('operation failed', cause);
    assert.equal(error.name, 'InvalidOperationException');
    assert.equal(error.message, 'operation failed');
    assert.equal(error.InnerException, cause);
    assert.ok(error instanceof Error);
});
