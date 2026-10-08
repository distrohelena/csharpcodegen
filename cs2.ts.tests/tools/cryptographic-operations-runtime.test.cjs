const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { CryptographicOperations } = require(path.join(root, 'system/security/cryptography/cryptographic-operations.ts'));
test('byte equality covers empty, equal, every mismatch position and unequal lengths', () => {
    assert.equal(CryptographicOperations.FixedTimeEquals(new Uint8Array(),new Uint8Array()),true);
    const original = Uint8Array.from({length:128},(_,i)=>i);
    assert.equal(CryptographicOperations.FixedTimeEquals(original,original.slice()),true);
    for (let i=0;i<original.length;i++) { const changed=original.slice(); changed[i]^=255; assert.equal(CryptographicOperations.FixedTimeEquals(original,changed),false); }
    assert.equal(CryptographicOperations.FixedTimeEquals(original,original.subarray(1)),false);
});
test('zeroing clears only the supplied span and accepts an empty span', () => {
    const bytes = new Uint8Array([1,2,3,4]); CryptographicOperations.ZeroMemory(bytes.subarray(1,3));
    assert.deepEqual([...bytes],[1,0,0,4]); CryptographicOperations.ZeroMemory(new Uint8Array());
});
