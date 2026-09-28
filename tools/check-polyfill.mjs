#!/usr/bin/env node
/**
 * BrowserForWP — ES5 conformance check for the polyfill bundle.
 *
 * WHY THIS EXISTS
 * ---------------
 * BrowserForWP.Polyfill/compat.js is injected into every document and runs on
 * Trident (IE11). If it ever contains ES6-only syntax, the shim fails to parse
 * on the only engine that needs it — and it fails silently, because the failure
 * happens inside a page the user is loading, not in our own code.
 *
 * A naive grep for "const"/"let"/"`" produces false positives in comments and
 * string literals, so this strips comments and literals before scanning, then
 * separately confirms the file still parses.
 *
 * Usage
 * -----
 *   node tools/check-polyfill.mjs [path]
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.dirname(HERE);
const target = process.argv[2] ?? path.join(ROOT, 'BrowserForWP.Polyfill', 'compat.js');

if (!fs.existsSync(target)) {
  console.error(`✗ not found: ${target}`);
  process.exit(1);
}

const source = fs.readFileSync(target, 'utf8');

/**
 * Remove comments and string literals, preserving line structure so reported
 * line numbers stay meaningful.
 */
function stripCommentsAndLiterals(src) {
  let out = '';
  let i = 0;
  const n = src.length;
  let state = 'code'; // code | line | block | single | double | regex-ish

  while (i < n) {
    const c = src[i];
    const next = src[i + 1];

    if (state === 'code') {
      if (c === '/' && next === '/') { state = 'line'; out += '  '; i += 2; continue; }
      if (c === '/' && next === '*') { state = 'block'; out += '  '; i += 2; continue; }
      if (c === "'") { state = 'single'; out += ' '; i += 1; continue; }
      if (c === '"') { state = 'double'; out += ' '; i += 1; continue; }
      out += c;
      i += 1;
      continue;
    }

    if (state === 'line') {
      if (c === '\n') { state = 'code'; out += '\n'; i += 1; continue; }
      out += ' ';
      i += 1;
      continue;
    }

    if (state === 'block') {
      if (c === '*' && next === '/') { state = 'code'; out += '  '; i += 2; continue; }
      out += c === '\n' ? '\n' : ' ';
      i += 1;
      continue;
    }

    // string literals
    if (c === '\\') { out += '  '; i += 2; continue; }
    if ((state === 'single' && c === "'") || (state === 'double' && c === '"')) {
      state = 'code';
      out += ' ';
      i += 1;
      continue;
    }
    out += c === '\n' ? '\n' : ' ';
    i += 1;
  }

  return out;
}

const code = stripCommentsAndLiterals(source);

// ES6+ constructs that Trident (IE11) cannot parse.
const forbidden = [
  [/\blet\s+[A-Za-z_$]/, 'let declaration'],
  [/\bconst\s+[A-Za-z_$]/, 'const declaration'],
  [/=>/, 'arrow function'],
  [/\bclass\s+[A-Za-z_$]/, 'class declaration'],
  [/\bclass\s*\{/, 'class expression'],
  [/\basync\s/, 'async keyword'],
  [/\bawait\s/, 'await keyword'],
  [/`/, 'template literal'],
  [/\.\.\./, 'spread/rest'],
  [/\byield\s/, 'generator yield'],
  [/\bfor\s*\(\s*\w+\s+of\s/, 'for...of'],
  [/\bexport\s+(default|const|function|class|\{)/, 'ES module export'],
  [/\bimport\s+[\w{*]/, 'ES module import'],
  [/Object\.entries\s*\(/, 'Object.entries call'],   // polyfilled here, must not be relied on
  [/Object\.values\s*\(/, 'Object.values call'],
  [/\.includes\s*\(/, 'String/Array.includes call'],  // polyfilled here, must not be relied on
  [/\bSymbol\s*\(/, 'Symbol'],
  [/new\s+Proxy\s*\(/, 'Proxy'],
];

// The polyfill legitimately *defines* these; only flag syntactic ES6, plus
// runtime use of APIs it defines later in the same file.
const definitionAllowances = [
  /define\s*\([^,]+,\s*'[^']*'/,   // define(target, 'name', ...) is how we install
];

const violations = [];
const lines = code.split('\n');
for (let lineNo = 0; lineNo < lines.length; lineNo++) {
  const line = lines[lineNo];
  if (!line.trim()) continue;
  if (definitionAllowances.some((re) => re.test(line))) continue;
  for (const [re, label] of forbidden) {
    if (re.test(line)) {
      violations.push({ line: lineNo + 1, label, text: source.split('\n')[lineNo].trim() });
    }
  }
}

// Independent check: the bundle must actually parse.
let parseError = null;
try {
  new Function(source);
} catch (e) {
  parseError = e;
}

const relative = path.relative(ROOT, target);

if (parseError) {
  console.error(`✗ ${relative} does not parse: ${parseError.message}`);
  process.exit(1);
}

if (violations.length) {
  console.error(`✗ ${relative} contains ${violations.length} ES6-only construct(s):`);
  for (const v of violations) {
    console.error(`   line ${v.line}: ${v.label}`);
    console.error(`     ${v.text}`);
  }
  console.error('\nIE11 cannot parse these, so the shim would fail on the one engine that needs it.');
  process.exit(1);
}

console.log(`✓ ${relative} is valid ES5 (${source.length} bytes, comments and literals excluded from the scan)`);
