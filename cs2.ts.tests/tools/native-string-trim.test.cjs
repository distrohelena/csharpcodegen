const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { NativeStringUtil } = require(path.join(root, 'system/util/nat-string-util.ts'));

test('NativeStringUtil trimCharacters implements .NET character-set trimming', () => {
    assert.equal(NativeStringUtil.trimCharacters('///route///', 'both', '/'), 'route');
    assert.equal(NativeStringUtil.trimCharacters('\\//route//', 'start', '/', '\\'), 'route//');
    assert.equal(NativeStringUtil.trimCharacters('route///', 'end', '/'), 'route');
    assert.equal(NativeStringUtil.trimCharacters(' \t route \n', 'both'), 'route');
});
