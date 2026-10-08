const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');

const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));

function importPath(file) {
    return path.join(root, file).replace(/\\/g, '/');
}

test('generated Lists and HashSets satisfy readonly collection and set contracts', () => {
    const fixture = path.join(os.tmpdir(), `cs2-collection-contract-${process.pid}.ts`);
    const source = [
        `import { List } from '${importPath('system/collections/generic/list')}';`,
        `import { HashSet } from '${importPath('system/collections/generic/hash-set')}';`,
        `import type { IReadOnlyCollection } from '${importPath('system/collections/generic/ireadonlycollection')}';`,
        `import type { ISet } from '${importPath('system/collections/generic/iset')}';`,
        'const values: IReadOnlyCollection<string> = new List<string>();',
        'const selected: ISet<string> = new HashSet<string>();',
        'values.Count;',
        'selected.Contains("entry");'
    ].join('\n');

    fs.writeFileSync(fixture, source);
    try {
        const program = ts.createProgram([fixture], {
            noEmit: true,
            strict: true,
            target: ts.ScriptTarget.ES2020,
            module: ts.ModuleKind.CommonJS,
            skipLibCheck: true
        });
        const diagnostics = ts.getPreEmitDiagnostics(program)
            .filter(diagnostic => diagnostic.category === ts.DiagnosticCategory.Error);
        assert.equal(diagnostics.length, 0, diagnostics.map(diagnostic => ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')).join('\n'));
    } finally {
        fs.rmSync(fixture, { force: true });
    }
});

test('generated IDictionary exposes the converter indexer operations', () => {
    const fixture = path.join(os.tmpdir(), `cs2-dictionary-contract-${process.pid}.ts`);
    const source = [
        `import { Dictionary } from '${importPath('system/collections/generic/dictionary')}';`,
        `import type { IDictionary } from '${importPath('system/collections/generic/dictionary.interface')}';`,
        'const values: IDictionary<string, string> = new Dictionary<string, string>();',
        'values.set("key", "value");',
        'const value: string | undefined = values.get("key");',
        'values.Remove("key");',
        'value;'
    ].join('\n');

    fs.writeFileSync(fixture, source);
    try {
        const program = ts.createProgram([fixture], {
            noEmit: true,
            strict: true,
            target: ts.ScriptTarget.ES2020,
            module: ts.ModuleKind.CommonJS,
            skipLibCheck: true
        });
        const diagnostics = ts.getPreEmitDiagnostics(program)
            .filter(diagnostic => diagnostic.category === ts.DiagnosticCategory.Error);
        assert.equal(diagnostics.length, 0, diagnostics.map(diagnostic => ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')).join('\n'));
    } finally {
        fs.rmSync(fixture, { force: true });
    }
});

test('Dictionary copy construction accepts readonly dictionaries and comparer order', () => {
    const fixture = path.join(os.tmpdir(), `cs2-dictionary-copy-${process.pid}.ts`);
    const source = [
        `import { Dictionary } from '${importPath('system/collections/generic/dictionary')}';`,
        `import type { IReadOnlyDictionary } from '${importPath('system/collections/generic/ireadonlydictionary')}';`,
        `import { StringComparer } from '${importPath('system/string-comparer')}';`,
        'const source: IReadOnlyDictionary<string, string> = new Dictionary<string, string>();',
        'const copied = new Dictionary<string, string>(source, StringComparer.Ordinal);',
        'copied.Count;'
    ].join('\n');

    fs.writeFileSync(fixture, source);
    try {
        const program = ts.createProgram([fixture], {
            noEmit: true,
            strict: true,
            target: ts.ScriptTarget.ES2020,
            module: ts.ModuleKind.CommonJS,
            skipLibCheck: true
        });
        const diagnostics = ts.getPreEmitDiagnostics(program)
            .filter(diagnostic => diagnostic.category === ts.DiagnosticCategory.Error);
        assert.equal(diagnostics.length, 0, diagnostics.map(diagnostic => ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')).join('\n'));
    } finally {
        fs.rmSync(fixture, { force: true });
    }
});
