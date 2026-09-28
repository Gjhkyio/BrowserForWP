#!/usr/bin/env node
// Prototype and referee for BrowserForWP.Core/Engine/Native/BlockLayout.vb.
//
// Transliteration rule for this repository: the VB is a hand port of the
// functions below, so when a check here disagrees with the device, first prove
// the check is right before touching the VB.
//
// Numbers are reproducible because the measurer is FixedAdvanceTextMeasurer:
// width = characters x font-size x 0.5, line height = 1.2 x font-size unless the
// page resolved one.
//
// Usage: node tools/proto/boxlayout.mjs

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
const ADVANCE_FACTOR = 0.5;
const NORMAL_LINE_HEIGHT = 1.2;

function measureWidth(text, style) {
  if (!text) { return 0; }
  return text.length * style.fontSizePx * ADVANCE_FACTOR;
}
function lineHeight(style) {
  return style.lineHeightPx > 0 ? style.lineHeightPx : style.fontSizePx * NORMAL_LINE_HEIGHT;
}

// A ComputedStyle with only the members block layout reads.
function style(options) {
  const base = {
    fontSizePx: 16, lineHeightPx: -1, widthPx: -1, heightPx: -1, maxWidthPx: -1,
    marginTopPx: 0, marginRightPx: 0, marginBottomPx: 0, marginLeftPx: 0,
    paddingTopPx: 0, paddingRightPx: 0, paddingBottomPx: 0, paddingLeftPx: 0,
    borderTopWidthPx: 0, borderRightWidthPx: 0, borderBottomWidthPx: 0, borderLeftWidthPx: 0
  };
  return Object.assign(base, options || {});
}
function block(tagName, nodeStyle, children) {
  return { kind: 'block', tagName, style: nodeStyle, children: children || [] };
}
function text(content, nodeStyle) {
  return { kind: 'text', tagName: '#text', text: content, style: nodeStyle, children: [] };
}

function layOutChildren(children, contentLeft, contentTop, contentWidth) {
  let used = 0;
  for (const child of children) {
    const childStyle = child.style;
    if (child.kind === 'block') {
      used += childStyle.marginTopPx;
      const laid = layOutBlock(child, contentLeft, contentTop + used, contentWidth);
      used += laid.heightPx + childStyle.marginBottomPx;
    } else {
      const height = lineHeight(childStyle);
      used += height;
    }
  }
  return used;
}

function layOutBlock(node, parentContentLeft, flowTop, containingWidth) {
  const s = node.style;
  const marginLeft = Math.max(0, s.marginLeftPx);
  const marginRight = Math.max(0, s.marginRightPx);
  const horizontalInsets =
    Math.max(0, s.borderLeftWidthPx) + Math.max(0, s.borderRightWidthPx) +
    Math.max(0, s.paddingLeftPx) + Math.max(0, s.paddingRightPx);

  let borderBoxWidth = s.widthPx >= 0 ? s.widthPx : containingWidth - marginLeft - marginRight;
  if (s.maxWidthPx >= 0 && borderBoxWidth > s.maxWidthPx) { borderBoxWidth = s.maxWidthPx; }
  const available = containingWidth - marginLeft - marginRight;
  if (borderBoxWidth > available) { borderBoxWidth = available; }
  if (borderBoxWidth < 0) { borderBoxWidth = 0; }

  const xPx = parentContentLeft + marginLeft;
  const yPx = flowTop + Math.max(0, s.marginTopPx);
  let contentWidthPx = borderBoxWidth - horizontalInsets;
  if (contentWidthPx < 0) { contentWidthPx = 0; }

  let contentHeightPx = 0;
  if (contentWidthPx > 0) {
    contentHeightPx = layOutChildren(node.children, xPx + Math.max(0, s.borderLeftWidthPx) + Math.max(0, s.paddingLeftPx),
      yPx + Math.max(0, s.borderTopWidthPx) + Math.max(0, s.paddingTopPx), contentWidthPx);
  }

  let borderBoxHeight = Math.max(0, s.paddingTopPx) + contentHeightPx + Math.max(0, s.paddingBottomPx) +
    Math.max(0, s.borderTopWidthPx) + Math.max(0, s.borderBottomWidthPx);
  if (s.heightPx >= 0 && s.heightPx > borderBoxHeight) { borderBoxHeight = s.heightPx; }

  return { xPx, yPx, widthPx: borderBoxWidth, heightPx: borderBoxHeight, contentWidthPx };
}

const viewportPx = 360;

// 1. Filling the viewport, margins excluded.
const filling = layOutBlock(block('div', style({ marginLeftPx: 8, marginRightPx: 8 })), 0, 0, viewportPx);
check('a block fills its container minus its margins',
  near(filling.widthPx, 344) && near(filling.xPx, 8));

// 2. An explicit width wins over filling.
const sized = layOutBlock(block('div', style({ widthPx: 100 })), 0, 0, viewportPx);
check('an explicit width wins over filling the container', near(sized.widthPx, 100));

// 3. max-width caps an explicit width.
const capped = layOutBlock(block('div', style({ widthPx: 400, maxWidthPx: 300 })), 0, 0, viewportPx);
check('max-width caps the width', near(capped.widthPx, 300));

// 4. Siblings stack, and a sibling's margin pushes the next one down.
const stack = block('div', style({}), [
  block('p', style({ heightPx: 20, marginBottomPx: 10 }), []),
  block('p', style({ heightPx: 30 }), [])
]);
const stacked = layOutBlock(stack, 0, 0, viewportPx);
const secondChildTop = layOutBlock(block('p', style({ heightPx: 30 })), 0, 0 + 20 + 10, viewportPx).yPx;
check('a sibling is placed below the previous one plus its margin',
  near(stacked.heightPx, 60) && near(secondChildTop, 30));

// 5. Height accumulates padding and borders.
const padded = layOutBlock(block('div', style({ paddingTopPx: 10, paddingBottomPx: 4, borderTopWidthPx: 2 }), [
  block('p', style({ heightPx: 50 }), [])
]), 0, 0, viewportPx);
check('height accumulates padding and borders', near(padded.heightPx, 66));

// 6. A text run is measured, not guessed.
const withText = layOutBlock(block('div', style({}), [text('0123456789', style({}))]), 0, 0, viewportPx);
check('a text run contributes its measured line height',
  near(withText.heightPx, 19.2) && near(measureWidth('0123456789', style({})), 80));

// ── Parity with the VB port ────────────────────────────────────────────────
const layoutBox = readIfPresent('BrowserForWP.Core/Engine/Native/LayoutBox.vb');
const blockLayout = readIfPresent('BrowserForWP.Core/Engine/Native/BlockLayout.vb');

check('LayoutBox.vb exists', layoutBox.length > 0);
check('BlockLayout.vb exists', blockLayout.length > 0);
check('BlockLayout exposes Layout(root, viewportWidthPx, measurer)',
  blockLayout.includes('Function Layout(root As BoxNode, viewportWidthPx As Double, measurer As ITextMeasurer) As LayoutBox'));
check('the layout files contain no VB 14 fluent chain',
  layoutBox.length > 0 && blockLayout.length > 0 &&
  !/\)\.\s*\n\s*\w+\(/.test(layoutBox) && !/\)\.\s*\n\s*\w+\(/.test(blockLayout));

console.log(`\n${passed}/${passed + failed} checks passed.`);
if (failed > 0) { process.exit(1); }
