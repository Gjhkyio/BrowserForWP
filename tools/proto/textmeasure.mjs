#!/usr/bin/env node
// Prototype and referee for the text-measurement seam.
//
// Why a prototype for five lines of arithmetic: every number the layout referee
// asserts is a multiple of a character advance, so the advance has to be a
// constant that a Node script and the VB can both state. FixedAdvanceTextMeasurer
// is that constant. If this file and the VB disagree, layout is wrong on the
// device and right in Node, which is the hardest kind of wrong to find.
//
// Usage: node tools/proto/textmeasure.mjs

import fs from 'node:fs';

let passed = 0;
let failed = 0;
function check(label, condition) {
  if (condition) { passed++; console.log(`  ok   ${label}`); }
  else { failed++; console.log(`  FAIL ${label}`); }
}
function near(actual, expected) { return Math.abs(actual - expected) < 1e-9; }
function readIfPresent(path) {
  return fs.existsSync(path) ? fs.readFileSync(path, 'utf8') : '';
}

// ── The prototype ──────────────────────────────────────────────────────────
const ADVANCE_FACTOR = 0.5;          // ems per character
const NORMAL_LINE_HEIGHT = 1.2;      // CSS's "normal"

function measureWidth(text, style) {
  if (!text) { return 0; }
  return text.length * style.fontSizePx * ADVANCE_FACTOR;
}
function lineHeight(style) {
  if (style.lineHeightPx > 0) { return style.lineHeightPx; }
  return style.fontSizePx * NORMAL_LINE_HEIGHT;
}
function styleOf(fontSizePx, lineHeightPx) {
  return { fontSizePx: fontSizePx, lineHeightPx: lineHeightPx === undefined ? -1 : lineHeightPx };
}

check('empty text has zero width', measureWidth('', styleOf(16)) === 0);
check('width is half an em per character', near(measureWidth('abcd', styleOf(16)), 32));
check('width scales with font size', near(measureWidth('abcd', styleOf(32)), 64));
check('normal line height is 1.2 x font size', near(lineHeight(styleOf(16)), 19.2));
check('a resolved line height wins over normal', near(lineHeight(styleOf(16, 22.4)), 22.4));

// ── Parity with the VB port ────────────────────────────────────────────────
const iface = readIfPresent('BrowserForWP.Core/Engine/Native/ITextMeasurer.vb');
const fixedMeasurer = readIfPresent('BrowserForWP.Core/Engine/Native/FixedAdvanceTextMeasurer.vb');
const xamlMeasurer = readIfPresent('BrowserForWP/Rendering/XamlTextMeasurer.vb');

check('ITextMeasurer.vb exists', iface.length > 0);
check('FixedAdvanceTextMeasurer.vb exists', fixedMeasurer.length > 0);
check('XamlTextMeasurer.vb exists', xamlMeasurer.length > 0);
check('the VB uses the same 0.5 advance',
  fixedMeasurer.includes('AdvanceFactor As Double = 0.5'));
check('the VB uses the same 1.2 normal line height',
  fixedMeasurer.includes('NormalLineHeightFactor As Double = 1.2'));

console.log(`\n${passed}/${passed + failed} checks passed.`);
if (failed > 0) { process.exit(1); }
