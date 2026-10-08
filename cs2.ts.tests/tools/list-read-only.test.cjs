const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));

require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);

const { List } = require(path.join(root, 'system/collections/generic/list.ts'));
const { ReadOnlyCollection } = require(path.join(root, 'system/collections/objectmodel/read-only-collection.ts'));
const { NotSupportedException } = require(path.join(root, 'system/not-supported.exception.ts'));

test('List.AsReadOnly returns a live read-only view rather than an array snapshot', () => {
    const values = new List([1, 2]);
    const view = values.AsReadOnly();

    assert.ok(view instanceof ReadOnlyCollection);
    assert.deepEqual([...view], [1, 2]);

    values.add(3);
    values[0] = 9;
    assert.equal(view.count, 3);
    assert.equal(view.get(0), 9);
    assert.deepEqual(view.map(value => value * 2), [18, 4, 6]);

    assert.throws(() => view.add(4), NotSupportedException);
    assert.throws(() => view.push(4), NotSupportedException);
    assert.throws(() => view.sort(), NotSupportedException);
    assert.throws(() => { view[0] = 4; }, NotSupportedException);
    assert.deepEqual([...values], [9, 2, 3]);
});
