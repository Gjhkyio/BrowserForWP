#!/usr/bin/env node
// Prototype and referee for CssParser.vb and SelectorMatcher.vb. Task 6 inserts
// the cascade section immediately before the parity marker below.
import fs from 'node:fs';

let failures = 0;
let checks = 0;
function check(name, ok, detail = '') {
  checks += 1;
  if (ok) { console.log(`  ✓ ${name}`); }
  else { console.log(`  ✗ ${name}${detail ? ': ' + detail : ''}`); failures++; }
}
function readIfPresent(path) {
  return fs.existsSync(path) ? fs.readFileSync(path, 'utf8') : '';
}

function stripComments(css) {
  let out = '';
  let i = 0;
  while (i < css.length) {
    if (css.startsWith('/*', i)) {
      const end = css.indexOf('*/', i + 2);
      i = end < 0 ? css.length : end + 2;
      continue;
    }
    out += css[i];
    i += 1;
  }
  return out;
}

function parseSimple(text) {
  const simple = { type: '', cls: '', id: '' };
  const re = /([.#]?)([A-Za-z0-9_-]+)/g;
  let m;
  while ((m = re.exec(text)) !== null) {
    if (m[1] === '.') simple.cls = m[2].toLowerCase();
    else if (m[1] === '#') simple.id = m[2].toLowerCase();
    else simple.type = m[2].toLowerCase();
  }
  return simple;
}

function parseSelector(text) {
  const parts = [];
  const tokens = text.trim().split(/\s*(>)\s*|\s+/).filter((t) => t !== undefined && t !== '');
  let combinator = '';
  for (const t of tokens) {
    if (t === '>') { combinator = '>'; continue; }
    parts.push({ combinator, simple: parseSimple(t) });
    combinator = ' ';
  }
  return parts;
}

function specificity(parts) {
  let total = 0;
  for (const p of parts) {
    if (p.simple.id) total += 100;
    if (p.simple.cls) total += 10;
    if (p.simple.type && p.simple.type !== '*') total += 1;
  }
  return total;
}

function parseCss(css) {
  const text = stripComments(css);
  const rules = [];
  let i = 0;
  while (i < text.length) {
    const braceAt = text.indexOf('{', i);
    if (braceAt < 0) break;
    const closeAt = text.indexOf('}', braceAt);
    if (closeAt < 0) break;
    const selectorText = text.slice(i, braceAt).trim();
    const body = text.slice(braceAt + 1, closeAt);
    i = closeAt + 1;
    if (!selectorText || selectorText.startsWith('@')) continue;
    const selectors = selectorText.split(',').map((s) => parseSelector(s)).filter((s) => s.length > 0);
    if (selectors.length === 0) continue;
    const declarations = [];
    for (const piece of body.split(';')) {
      const colon = piece.indexOf(':');
      if (colon <= 0) continue;
      declarations.push({ name: piece.slice(0, colon).trim().toLowerCase(), value: piece.slice(colon + 1).trim() });
    }
    if (declarations.length === 0) continue;
    rules.push({ selectors, declarations });
  }
  return rules;
}

const sheet = parseCss(`
  /* comment */
  p { color: #111; margin: 8px 4px }
  .lead { color: red }
  #main p { color: blue }
  div > p { font-size: 14px }
  h1, h2 { font-weight: bold }
  @media screen { p { color: green } }
`);

check('comment stripped', !JSON.stringify(sheet).includes('comment'));
check('five rules parsed, at-rule skipped', sheet.length === 5);
check('declarations split', sheet[0].declarations.length === 2);
check('declaration name lowercased', sheet[0].declarations[0].name === 'color');
check('declaration value preserved', sheet[0].declarations[1].value === '8px 4px');
check('selector list yields two selectors', sheet[4].selectors.length === 2);
check('type specificity', specificity(parseSelector('p')) === 1);
check('class specificity', specificity(parseSelector('.lead')) === 10);
check('id specificity', specificity(parseSelector('#main')) === 100);
check('id plus type', specificity(parseSelector('#main p')) === 101);
check('class plus type', specificity(parseSelector('p.lead')) === 11);
check('universal adds nothing', specificity(parseSelector('*')) === 0);
check('child combinator recorded', (() => {
  const parts = parseSelector('div > p');
  return parts.length === 2 && parts[1].combinator === '>';
})());
check('descendant combinator recorded', (() => {
  const parts = parseSelector('div p');
  return parts.length === 2 && parts[1].combinator === ' ';
})());

// ── matching, over a hand-built tree ────────────────────────────────────────
function el(tag, attrs = {}, children = []) {
  const node = { tagName: tag, attributes: attrs, children, text: '', parent: null };
  for (const c of children) c.parent = node;
  return node;
}
const tree = el('body', {}, [
  el('div', { id: 'main', class: 'wrap' }, [el('p', { class: 'lead' }, [el('em', {}, [])])]),
  el('p', { class: 'lead' }, []),
]);

function matchesSimple(simple, node) {
  if (!node || node.tagName === '#text') return false;
  if (simple.type && simple.type !== '*' && node.tagName !== simple.type) return false;
  if (simple.id && node.attributes.id !== simple.id) return false;
  if (simple.cls) {
    const classes = (node.attributes.class || '').split(/\s+/).filter(Boolean);
    if (!classes.includes(simple.cls)) return false;
  }
  return true;
}

function matches(selector, node) {
  if (!node || node.tagName === '#text') return false;
  let current = node;
  for (let i = selector.length - 1; i >= 0; i -= 1) {
    if (!matchesSimple(selector[i].simple, current)) return false;
    if (i === 0) return true;
    const wantChild = selector[i].combinator === '>';
    current = current.parent;
    if (wantChild) {
      if (!current) return false;
    } else {
      // Descendant: climb until an ancestor matches the part on the left.
      let climbed = current;
      while (climbed && !matchesSimple(selector[i - 1].simple, climbed)) climbed = climbed.parent;
      if (!climbed) return false;
      current = climbed;
      i -= 1;   // the climb consumed the left part, so the loop must skip it too
    }
  }
  return true;
}

const innerP = tree.children[0].children[0];
const outerP = tree.children[1];
check('type matches', matches(parseSelector('p'), innerP) === true);
check('type does not match other tag', matches(parseSelector('div'), innerP) === false);
check('descendant matches', matches(parseSelector('div p'), innerP) === true);
check('descendant does not match unrelated', matches(parseSelector('section p'), outerP) === false);
check('child matches', matches(parseSelector('body > div'), tree.children[0]) === true);
check('child does not match grandchild', matches(parseSelector('body > p'), innerP) === false);
check('id matches', matches(parseSelector('#main'), tree.children[0]) === true);
check('class matches', matches(parseSelector('.lead'), outerP) === true);
check('class does not match missing class', matches(parseSelector('.other'), outerP) === false);
check('id plus descendant', matches(parseSelector('#main p'), innerP) === true);
check('em is reachable by class from an ancestor', matches(parseSelector('.wrap em'), innerP.children[0]) === true);

// ── VB file parity (CssParser) ──────────────────────────────────────────────
const parser = readIfPresent('BrowserForWP.Core/Engine/Native/CssParser.vb');
check('CssParser.vb exists', parser.length > 0);
check('CssParser.vb parses into a Stylesheet', parser.includes('Stylesheet'));
check('CssParser.vb does not use RegexOptions.Compiled',
  parser.length > 0 && !parser.includes('RegexOptions.Compiled'));

console.log(`\n${checks - failures}/${checks} csscascade checks passed.`);
if (failures > 0) {
  console.log(`${failures} csscascade failure(s).`);
  process.exit(1);
}
