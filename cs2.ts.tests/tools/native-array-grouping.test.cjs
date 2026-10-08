const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { NativeArrayUtil } = require(path.join(root, 'system/util/nat-array-util.ts'));
const { StringComparer } = require(path.join(root, 'system/string-comparer.ts'));
const { ArgumentException } = require(path.join(root, 'system/argument.exception.ts'));

test('NativeArrayUtil groups by comparer, preserves first-key order, and materializes dictionary values', () => {
    const source = [{ family: 'alpha', id: 1 }, { family: 'BETA', id: 2 }, { family: 'ALPHA', id: 3 }];
    const groups = NativeArrayUtil.groupBy(source, item => item.family, StringComparer.OrdinalIgnoreCase);
    assert.deepEqual(groups.map(group => group.Key), ['alpha', 'BETA']);
    assert.deepEqual(groups[0].toList().map(item => item.id), [1, 3]);
    const grouped = NativeArrayUtil.toDictionary(groups, group => group.Key, group => group.toList(), StringComparer.OrdinalIgnoreCase);
    assert.deepEqual(grouped.get('ALPHA').map(item => item.id), [1, 3]);
    assert.throws(() => NativeArrayUtil.toDictionary([{ id: 1 }, { id: 1 }], item => item.id), ArgumentException);
});

test('NativeArrayUtil ToDictionary infers the value-selector result type', () => {
    const fixture = path.join(require('node:os').tmpdir(), `cs2-grouping-contract-${process.pid}.ts`);
    fs.writeFileSync(fixture, [
        `import { NativeArrayUtil } from '${path.join(root, 'system/util/nat-array-util').replace(/\\/g, '/')}';`,
        `import { Dictionary } from '${path.join(root, 'system/collections/generic/dictionary').replace(/\\/g, '/')}';`,
        'const source = [{ key: "family", value: 1 }];',
        'const result: Dictionary<string, string[]> = NativeArrayUtil.toDictionary(source, item => item.key, item => [String(item.value)], null);',
        'result;'
    ].join('\n'));
    try {
        const program = ts.createProgram([fixture], { noEmit: true, strict: true, target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS, skipLibCheck: true });
        const diagnostics = ts.getPreEmitDiagnostics(program).filter(diagnostic => diagnostic.category === ts.DiagnosticCategory.Error);
        assert.equal(diagnostics.length, 0, diagnostics.map(diagnostic => ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')).join('\n'));
    } finally {
        fs.rmSync(fixture, { force: true });
    }
});