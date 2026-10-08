const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { Rfc2898DeriveBytes } = require(path.join(root, 'system/security/cryptography/rfc-2898-derive-bytes.ts'));
for (const hash of ['sha1','sha256','sha384','sha512']) test(`static PBKDF2 ${hash} matches native crypto for empty, binary and UTF8 inputs`, () => {
    for (const password of [new Uint8Array(), new Uint8Array([0,255,1,0]), new TextEncoder().encode('päss😀')]) {
        const salt = new Uint8Array([0,1,255,2]);
        for (const size of [0,1,32,65]) {
            const expected = size === 0 ? Buffer.alloc(0) : crypto.pbkdf2Sync(password,salt,3,size,hash);
            const actual = Rfc2898DeriveBytes.Pbkdf2(password,salt,3,hash,size);
            assert.deepEqual(Buffer.from(actual), expected);
        }
    }
});
test('static PBKDF2 validates CLR argument boundaries and supports UTF8 password overload', () => {
    const bytes = new Uint8Array();
    assert.throws(() => Rfc2898DeriveBytes.Pbkdf2(null,bytes,1,'sha256',1), error => error.ParamName === 'password');
    assert.throws(() => Rfc2898DeriveBytes.Pbkdf2(bytes,null,1,'sha256',1), error => error.ParamName === 'salt');
    for (const iterations of [0,-1,1.5,2147483648]) assert.throws(() => Rfc2898DeriveBytes.Pbkdf2(bytes,bytes,iterations,'sha256',1));
    for (const size of [-1,1.5,2147483648]) assert.throws(() => Rfc2898DeriveBytes.Pbkdf2(bytes,bytes,1,'sha256',size));
    assert.throws(() => Rfc2898DeriveBytes.Pbkdf2(bytes,bytes,1,'md5',1));
    assert.deepEqual(Buffer.from(Rfc2898DeriveBytes.Pbkdf2('päss😀',bytes,2,'sha256',32)),crypto.pbkdf2Sync('päss😀',bytes,2,32,'sha256'));
});
