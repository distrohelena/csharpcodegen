const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
const exportsObject = {};
const source = fs.readFileSync(path.join(root, 'system/util/nat-array-util.ts'), 'utf8');
const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
// No Node Buffer is present: the runtime must work before any browser polyfill is installed.
vm.runInNewContext(output, { exports: exportsObject, Uint8Array, ArrayBuffer });
const { NativeArrayUtil } = exportsObject;

test('copies exact byte offsets without any Buffer global', () => {
    const source = new Uint8Array([9, 10, 11, 12, 13]).subarray(1, 4);
    const backing = new Uint8Array([90, 91, 92, 93, 94]);
    NativeArrayUtil.blockCopy(source, 1, backing.subarray(1, 4), 1, 2);
    assert.deepEqual(Array.from(backing), [90, 91, 11, 12, 94]);
});

test('uses byte offsets for nonbyte primitive arrays', () => {
    const source = new Uint16Array([0x1234, 0x5678]);
    const bytes = new Uint8Array(source.buffer);
    const result = new Uint8Array(3);
    NativeArrayUtil.blockCopy(source, 1, result, 0, 3);
    assert.deepEqual(Array.from(result), Array.from(bytes.subarray(1)));
});

test('preserves overlapping ranges in either direction', () => {
    const bytes = new Uint8Array([1, 2, 3, 4, 5]);
    NativeArrayUtil.blockCopy(bytes, 0, bytes, 1, 4);
    assert.deepEqual(Array.from(bytes), [1, 1, 2, 3, 4]);
    NativeArrayUtil.blockCopy(bytes, 1, bytes, 0, 4);
    assert.deepEqual(Array.from(bytes), [1, 2, 3, 4, 4]);
});

for (const invalid of [-1, 0.5, NaN, Infinity, undefined, null]) {
    for (const parameter of [0, 1, 2]) {
        test(`rejects invalid offset/count ${String(invalid)} at ${parameter} without changing destination`, () => {
            const destination = new Uint8Array([8, 9]);
            const values = [0, 0, 1];
            values[parameter] = invalid;
            assert.throws(() => NativeArrayUtil.blockCopy(new Uint8Array([1, 2]), values[0], destination, values[1], values[2]));
            assert.deepEqual(Array.from(destination), [8, 9]);
        });
    }
}

test('rejects invalid buffers and out-of-bounds copies before writing', () => {
    const destination = new Uint8Array([8, 9]);
    for (const source of [null, undefined, [1, 2], {}]) {
        assert.throws(() => NativeArrayUtil.blockCopy(source, 0, destination, 0, 1));
    }
    assert.throws(() => NativeArrayUtil.blockCopy(new Uint8Array(2), 1, destination, 0, 2));
    assert.throws(() => NativeArrayUtil.blockCopy(new Uint8Array(2), 0, destination, 1, 2));
    assert.deepEqual(Array.from(destination), [8, 9]);
    NativeArrayUtil.blockCopy(destination, 2, destination, 2, 0);
});
