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
const { HashSet } = require(path.join(root, 'system/collections/generic/hash-set.ts'));
const { StringComparer } = require(path.join(root, 'system/string-comparer.ts'));

test('LINQ equality operations honor StringComparer instead of JavaScript identity', () => {
    const values = new List(['Alpha', 'alpha', 'Beta']);
    assert.equal(values.contains('ALPHA', StringComparer.OrdinalIgnoreCase), true);
    assert.equal(values.Contains('ALPHA', StringComparer.Ordinal), false);
    assert.deepEqual(values.Distinct(StringComparer.OrdinalIgnoreCase), ['Alpha', 'Beta']);
    assert.equal(['Alpha', 'beta'].SequenceEqual(['alpha', 'BETA'], StringComparer.OrdinalIgnoreCase), true);
    assert.equal(new HashSet(['Alpha']).Contains('alpha', StringComparer.OrdinalIgnoreCase), true);
});

test('LINQ ordering honors comparer and keeps chained primary and secondary ordering', () => {
    const values = [
        { group: 'beta', id: 'b' },
        { group: 'Alpha', id: 'z' },
        { group: 'alpha', id: 'a' },
        { group: 'Beta', id: 'a' }
    ];
    const ordered = values
        .OrderBy(value => value.group, StringComparer.OrdinalIgnoreCase)
        .ThenBy(value => value.id, StringComparer.Ordinal);
    assert.deepEqual(ordered.map(value => value.group + ':' + value.id), ['alpha:a', 'Alpha:z', 'Beta:a', 'beta:b']);
});
