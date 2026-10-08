const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
/** Executes the maintained runtime source directly, before generation into any consumer. */
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { HashSet } = require(path.join(root, 'system/collections/generic/hash-set.ts'));
const { List } = require(path.join(root, 'system/collections/generic/list.ts'));

test('materialization preserves set enumeration and produces an independent List', () => {
    const source = new HashSet(['account-b', 'account-a', 'account-b']);
    const snapshot = source.toList();
    assert.ok(snapshot instanceof List);
    assert.deepEqual([...snapshot], [...source]);
    source.Add('account-c');
    snapshot.add('account-d');
    snapshot.remove('account-a');
    assert.deepEqual([...source], ['account-b', 'account-a', 'account-c']);
    assert.deepEqual([...snapshot], ['account-b', 'account-d']);
});

test('materialization handles empty and single numeric sets without interpreting items as capacity', () => {
    assert.deepEqual([...new HashSet().toList()], []);
    assert.deepEqual([...new HashSet([7]).toList()], [7]);
    assert.deepEqual([...new HashSet([0]).toList()], [0]);
});

test('materialization preserves collisions, comparer deduplication and item identity', () => {
    const comparer = { GetHashCode: () => 1, Equals: (a, b) => a.id.toLowerCase() === b.id.toLowerCase() };
    const first = { id: 'Account-A' };
    const second = { id: 'Account-B' };
    const source = new HashSet([first, { id: 'account-a' }, second], comparer);
    const snapshot = source.toList();
    assert.equal(snapshot.count, 2);
    assert.equal(snapshot[0], first);
    assert.equal(snapshot[1], second);
    source.Clear();
    assert.equal(snapshot.count, 2);
});
