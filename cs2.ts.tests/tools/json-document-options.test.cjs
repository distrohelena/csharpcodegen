const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
const previousLoader = require.extensions['.ts'];
require.extensions['.ts'] = (module, filename) => {
    const source = fs.readFileSync(filename, 'utf8');
    module._compile(ts.transpileModule(source, { compilerOptions: {
        module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
    } }).outputText, filename);
};
const { JsonTextParser } = require(path.join(root, 'system/text/json/json-text-parser.ts'));
const { JsonDocumentOptions } = require(path.join(root, 'system/text/json/json-document-options.ts'));
const { JsonCommentHandling } = require(path.join(root, 'system/text/json/json-comment-handling.ts'));
const { ArgumentOutOfRangeException } = require(path.join(root, 'system/argument-out-of-range.exception.ts'));
if (previousLoader) require.extensions['.ts'] = previousLoader;
else delete require.extensions['.ts'];

test('preserves string contents, escaped quotes, backslashes, and comment-like text', () => {
    const expected = { url: 'https://example/a,}', quote: '"/* text */', slash: '\\', list: [1, 2] };
    const json = JSON.stringify(expected);
    assert.deepEqual(JsonTextParser.Parse(json, true, true), expected);
    assert.deepEqual(JsonTextParser.Parse('/*header*/' + json.slice(0, -1) + ',/*tail*/}// end\n', true, true), expected);
});

test('handles line endings and valid trailing array/object commas independently', () => {
    for (const ending of ['\n', '\r', '\r\n']) {
        assert.deepEqual(JsonTextParser.Parse('[1,// comment' + ending + '2,]', true, true), [1, 2]);
    }
    assert.deepEqual(JsonTextParser.Parse('{"a":1,}', false, true), { a: 1 });
    assert.deepEqual(JsonTextParser.Parse('/*comment*/[1]', true, false), [1]);
    assert.throws(() => JsonTextParser.Parse('/*comment*/[1]', false, true));
    assert.throws(() => JsonTextParser.Parse('[1,]', true, false));
});

for (const input of ['[,]', '[1,,]', '{,}', '{"a":1,,}', '{"a":,}', '1/*split*/2', '/*unfinished', '{"a":"unfinished}', '/**/', '', ' ']) {
    test('rejects invalid JSON even with relaxed options: ' + JSON.stringify(input), () => {
        assert.throws(() => JsonTextParser.Parse(input, true, true));
    });
}

test('defaults to rejecting comments and refuses reader-only or invalid document modes', () => {
    const options = new JsonDocumentOptions();
    assert.equal(options.CommentHandling, JsonCommentHandling.Disallow);
    assert.equal(options.AllowTrailingCommas, false);
    options.CommentHandling = JsonCommentHandling.Skip;
    assert.equal(options.CommentHandling, JsonCommentHandling.Skip);
    for (const mode of [JsonCommentHandling.Allow, -1, 3, 0.5]) {
        assert.throws(() => { options.CommentHandling = mode; }, ArgumentOutOfRangeException);
        assert.equal(options.CommentHandling, JsonCommentHandling.Skip);
    }
});
