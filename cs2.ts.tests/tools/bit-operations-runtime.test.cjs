const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { BitOperations } = require(path.join(root, 'system/numerics/bit-operations.ts'));
test('uint32 leading zero count covers zero, every bit transition and maximum', () => {
    assert.equal(BitOperations.LeadingZeroCount(0),32);
    for (let bit=0;bit<32;bit++) {
        assert.equal(BitOperations.LeadingZeroCount(2**bit),31-bit);
        assert.equal(BitOperations.LeadingZeroCount(2**bit-1),32-bit);
    }
    assert.equal(BitOperations.LeadingZeroCount(0xffffffff),0);
});
test('all byte XOR distances choose the expected Kademlia band', () => {
    for (let value=1;value<=255;value++) {
        const highest = value.toString(2).length-1;
        for (let byteIndex=0;byteIndex<20;byteIndex++) {
            const actual = byteIndex*8+31-BitOperations.LeadingZeroCount(value);
            assert.equal(actual, byteIndex*8+highest);
        }
    }
});
