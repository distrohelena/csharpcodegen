const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
const exportsObject = {};
const source = fs.readFileSync(path.join(root, 'system/util/nat-number-util.ts'), 'utf8');
const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
vm.runInNewContext(output, { exports: exportsObject });
const { NativeNumberUtil } = exportsObject;

// Ordered .NET equivalence groups include NaN, infinities and both signs of zero.
const ordered = [[NaN, NaN], [-Infinity], [-Number.MAX_VALUE], [-1], [-Number.MIN_VALUE], [-0, 0], [Number.MIN_VALUE], [1], [Number.MAX_VALUE], [Infinity]];
test('matches numeric ordering across every pair of equivalence groups', () => {
    for (let i = 0; i < ordered.length; i++) {
        for (let j = 0; j < ordered.length; j++) {
            for (const left of ordered[i]) {
                for (const right of ordered[j]) {
                    assert.equal(NativeNumberUtil.compareTo(left, right), Math.sign(i - j));
                }
            }
        }
    }
});

test('sorts nonfinite and finite values using a total order', () => {
    const actual = [Infinity, 2, NaN, -3, -Infinity, NaN, 0].sort(NativeNumberUtil.compareTo);
    assert.deepEqual(actual, [NaN, NaN, -Infinity, -3, 0, 2, Infinity]);
});

test('evaluates operands once and left to right', () => {
    const calls = [];
    assert.equal(NativeNumberUtil.compareTo((calls.push('left'), 3), (calls.push('right'), 2)), 1);
    assert.deepEqual(calls, ['left', 'right']);
});
