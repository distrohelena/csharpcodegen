const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));

function importPath(file) { return path.join(root, file).replace(/\\/g, '/'); }

test('DateTime ToString accepts the .NET format-provider overload', () => {
    const fixture = path.join(os.tmpdir(), `cs2-date-time-provider-${process.pid}.ts`);
    fs.writeFileSync(fixture, [
        `import { DateTime } from '${importPath('system/date-time')}';`,
        'const value = new DateTime(2026, 10, 1);',
        'value.ToString("O", {});'
    ].join('\n'));
    try {
        const program = ts.createProgram([fixture], { noEmit: true, strict: true, target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS, skipLibCheck: true });
        const diagnostics = ts.getPreEmitDiagnostics(program).filter(diagnostic => diagnostic.category === ts.DiagnosticCategory.Error);
        assert.equal(diagnostics.length, 0, diagnostics.map(diagnostic => ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')).join('\n'));
    } finally {
        fs.rmSync(fixture, { force: true });
    }
});
