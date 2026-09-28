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
check('a text run is measured, not guessed',
  near(withText.heightPx, 19.2) && near(measureWidth('0123456789', style({})), 80));

// ── Inline flow ────────────────────────────────────────────────────────────
// One TextRun child per word: we break the lines ourselves, so the renderer draws
// words, not lines. Gaps between words are the space's own measured width.

function splitWords(text) {
  if (!text) { return []; }
  return text.split(/\s+/).filter(Boolean);
}

function buildLines(runs, contentLeftPx, flowTopPx, contentWidthPx, containerStyle) {
  const lines = [];
  if (!runs.length || contentWidthPx <= 0) { return lines; }

  const words = [];
  for (const run of runs) {
    if (!run.style) { continue; }
    for (const piece of splitWords(run.text)) {
      words.push({ text: piece, style: run.style });
    }
  }
  if (!words.length) { return lines; }

  const lineHeightPx = lineHeight(containerStyle || words[0].style);
  const groups = [];
  let current = { words: [], widthPx: 0 };

  for (const word of words) {
    const wordWidthPx = measureWidth(word.text, word.style);
    const spaceWidthPx = current.words.length > 0 ? measureWidth(' ', word.style) : 0;
    if (current.words.length > 0 && current.widthPx + spaceWidthPx + wordWidthPx > contentWidthPx) {
      groups.push(current);
      current = { words: [word], widthPx: wordWidthPx };
    } else {
      current.widthPx += spaceWidthPx + wordWidthPx;
      current.words.push(word);
    }
  }
  if (current.words.length) { groups.push(current); }

  groups.forEach((group, index) => {
    const lineTopPx = flowTopPx + index * lineHeightPx;
    const align = (containerStyle && containerStyle.textAlign) || 'left';
    let offsetPx = 0;
    if (align === 'center') { offsetPx = (contentWidthPx - group.widthPx) / 2; }
    else if (align === 'right') { offsetPx = contentWidthPx - group.widthPx; }
    if (offsetPx < 0) { offsetPx = 0; }

    let cursorX = contentLeftPx + offsetPx;
    const children = [];
    group.words.forEach((word, wordIndex) => {
      if (wordIndex > 0) { cursorX += measureWidth(' ', word.style); }
      const wordWidthPx = measureWidth(word.text, word.style);
      children.push({ xPx: cursorX, yPx: lineTopPx, widthPx: wordWidthPx, heightPx: lineHeightPx, text: word.text });
      cursorX += wordWidthPx;
    });
    lines.push({ xPx: contentLeftPx, yPx: lineTopPx, widthPx: contentWidthPx, heightPx: lineHeightPx, groupWidthPx: group.widthPx, children });
  });
  return lines;
}

// 7. A short run stays on one line.
const shortRun = buildLines([text('hello world', style({}))], 0, 0, 360, style({}));
check('a run that fits stays on one line',
  shortRun.length === 1 && shortRun[0].children.length === 2 && near(shortRun[0].heightPx, 19.2));

// 8. A long run wraps, and the line count is the arithmetic one.
//    20 words of 5 characters at 16px = 40px each, plus a space of 8px.
//    Line capacity: floor((360 + 8) / 48) = 7 words.
const longWords = [];
for (let i = 0; i < 20; i++) { longWords.push('abcde'); }
const wrapped = buildLines([text(longWords.join(' '), style({}))], 0, 0, 360, style({}));
check('a run that does not fit wraps into lines',
  wrapped.length === 3 && wrapped[2].children.length === 6);

// 9. Breaking happens at a space, never inside a word.
const singleLongWord = buildLines([text('abcdefghijklmnopqrstuvwxyz', style({}))], 0, 0, 100, style({}));
check('a word wider than the line is not split',
  singleLongWord.length === 1 && singleLongWord[0].children.length === 1 &&
  near(singleLongWord[0].children[0].widthPx, 208));

// 10. text-align centres and right-aligns by offsetting the line's own width.
const centred = buildLines([text('hello world', style({}))], 0, 0, 360, style({ textAlign: 'center' }));
check('text-align center offsets the words', near(centred[0].children[0].xPx, (360 - 88) / 2));
const rightAligned = buildLines([text('hello world', style({}))], 0, 0, 360, style({ textAlign: 'right' }));
check('text-align right offsets the words', near(rightAligned[0].children[0].xPx, 360 - 88));

// 11. A resolved line-height sets the line box, not the 1.2 default.
const tallLines = buildLines([text('hello', style({ fontSizePx: 20 }))], 0, 0, 360, style({ fontSizePx: 20, lineHeightPx: 28 }));
check('a resolved line-height wins over the default',
  tallLines.length === 1 && near(tallLines[0].heightPx, 28) && near(tallLines[0].children[0].yPx, 0));

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

const inlineLayout = readIfPresent('BrowserForWP.Core/Engine/Native/InlineLayout.vb');

check('InlineLayout.vb exists', inlineLayout.length > 0);
check('InlineLayout exposes BuildLines',
  inlineLayout.includes('Function BuildLines(runs As IList(Of BoxNode), contentLeftPx As Double, flowTopPx As Double, contentWidthPx As Double, containerStyle As ComputedStyle, measurer As ITextMeasurer) As IList(Of LayoutBox)'));
check('InlineLayout contains no VB 14 fluent chain', inlineLayout.length > 0 && !/\).\s*\n\s*\w+\(/.test(inlineLayout));

console.log(`\n${passed}/${passed + failed} checks passed.`);
if (failed > 0) { process.exit(1); }
