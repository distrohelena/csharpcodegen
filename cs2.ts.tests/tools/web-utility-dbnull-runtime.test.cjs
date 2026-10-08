const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { WebUtility } = require(path.join(root, 'system/net/web-utility.ts'));
const { DBNull } = require(path.join(root, 'system/dbnull.ts'));

test('HTML encoding preserves null and escapes markup without double-decoding entities', () => {
    assert.equal(WebUtility.HtmlEncode(null), null);
    assert.equal(WebUtility.HtmlEncode(''), '');
    assert.equal(WebUtility.HtmlEncode('<a title="x&y">\'é\'</a>'), '&lt;a title=&quot;x&amp;y&quot;&gt;&#39;&#233;&#39;&lt;/a&gt;');
    assert.equal(WebUtility.HtmlEncode('&amp;'), '&amp;amp;');
    assert.equal(WebUtility.HtmlEncode('Ā中\t\n'), 'Ā中\t\n');
});

test('database null has one typed identity distinct from null and ordinary objects', () => {
    assert.equal(DBNull.Value, DBNull.Value);
    assert.equal(DBNull.Value instanceof DBNull, true);
    assert.notEqual(DBNull.Value, null);
    assert.notEqual(DBNull.Value, {});
    assert.equal(DBNull.Value.ToString(), '');
    assert.equal(DBNull.Value.toString(), '');
});

test('HTML encoding matches the .NET oracle when supplied', { skip: !process.env.SSN_HTML_PARITY_FIXTURE }, () => {
    const fixture = JSON.parse(fs.readFileSync(process.env.SSN_HTML_PARITY_FIXTURE, 'utf8'));
    for (const row of fixture) {
        const input = row.Units === null ? null : row.Units.map(value => String.fromCharCode(value)).join('');
        assert.equal(WebUtility.HtmlEncode(input), row.Expected, row.Name);
    }
});