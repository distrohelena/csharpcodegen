const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));

require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);

const { AesGcm } = require(path.join(root, 'system/security/cryptography/aes-gcm.ts'));

test('AesGcm constructor tag-size overload drives encryption and decryption', async () => {
    for (const tagLength of [12, 13, 14, 15, 16]) {
        const aes = new AesGcm(new Uint8Array(32).fill(7), tagLength);
        const iv = new Uint8Array(12).fill(2);
        const plain = new Uint8Array([1, 2, 3, 4]);
        const ciphertext = new Uint8Array(plain.length);
        const tag = new Uint8Array(tagLength);

        await aes.encrypt(iv, plain, ciphertext, tag);
        const decrypted = new Uint8Array(plain.length);
        await aes.decrypt(iv, ciphertext, tag, decrypted);
        assert.deepEqual([...decrypted], [...plain]);
    }
});

test('AesGcm rejects tag sizes unavailable to Web Crypto', () => {
    assert.throws(() => new AesGcm(new Uint8Array(32), 10), RangeError);
    assert.throws(() => new AesGcm(new Uint8Array(32), 8), RangeError);
});
