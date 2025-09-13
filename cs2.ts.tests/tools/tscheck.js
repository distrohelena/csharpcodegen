#!/usr/bin/env node
// TypeScript syntax+type checker for a single TS file path.
// Usage: node tscheck.js <tsRootWithTypescript> <file.ts>

const fs = require('fs');
const path = require('path');

const tsRoot = process.argv[2];
const file = process.argv[3];
if (!tsRoot || !file) {
  console.error('usage: node tscheck.js <typescriptRoot> <file.ts>');
  process.exit(2);
}

let ts;
try {
  ts = require(path.join(tsRoot, 'node_modules', 'typescript'));
} catch (e) {
  try {
    ts = require('typescript');
  } catch (e2) {
    console.error('Could not load TypeScript. Run `npm install` in', tsRoot);
    process.exit(2);
  }
}

const options = {
  target: ts.ScriptTarget.ES2020,
  module: ts.ModuleKind.CommonJS,
  strict: false,
  noEmit: true,
  skipLibCheck: true,
  esModuleInterop: true,
};

const host = ts.createCompilerHost(options);
host.readFile = (f) => fs.readFileSync(f, 'utf8');
host.fileExists = (f) => fs.existsSync(f);

const program = ts.createProgram([file], options, host);
const diagnostics = ts.getPreEmitDiagnostics(program);

if (diagnostics.length > 0) {
  const formatHost = {
    getCanonicalFileName: (f) => f,
    getCurrentDirectory: ts.sys.getCurrentDirectory,
    getNewLine: () => ts.sys.newLine,
  };
  const msg = ts.formatDiagnosticsWithColorAndContext(diagnostics, formatHost);
  console.error(msg);
  process.exit(1);
} else {
  console.log('OK');
}

