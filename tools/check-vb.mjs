#!/usr/bin/env node
// ═══════════════════════════════════════════════════════════════════════════
//  Static VB.NET structural checker — BrowserForWP
//
//  WHY THIS EXISTS
//  ---------------
//  The Windows Phone 8.1 SDK cannot be installed on this project's development
//  host (an Apple silicon Mac), and the available ARM64 Windows 11 VM cannot
//  host Visual Studio 2013 either — Microsoft does not support pre-17.4 Visual
//  Studio on Arm-based processors, and the WP8.1 SDK's MSBuild targets were
//  never ported to Arm64 hosts.
//
//  That leaves a real gap: nobody has compiled these sources. Rather than accept
//  "untested", this tool performs the subset of compiler checks that can be
//  reproduced exactly, deterministically, on any machine:
//
//    1. Block balance          every Class/Sub/If/Try/... has its terminator
//    2. Implements completeness an `Implements IDisposable` needs matching members
//    3. Project/disk parity    every .vb on disk is in the .vbproj and vice versa
//    4. Namespace resolution   RootNamespace + declared Namespace actually produces
//                              the namespace that other projects import
//    5. Cross-project imports  every `Imports BrowserForWP.X` names a real namespace
//    6. Resource parity        en-US and it-IT define exactly the same keys
//    7. XAML handler wiring    every event handler named in XAML exists in VB
//
//  WHAT IT CANNOT DO
//  -----------------
//  It is not a compiler. It cannot type-check, resolve overloads, verify WinRT
//  API availability, or catch a wrong method signature. A clean run means "no
//  mechanical defects of these seven kinds", not "compiles". Do not let a green
//  run here substitute for an actual build on Windows.
//
//  USAGE
//    node tools/check-vb.mjs          # report, exit 1 on any finding
//    node tools/check-vb.mjs --quiet  # only print the summary
// ═══════════════════════════════════════════════════════════════════════════

import fs from 'node:fs';
import path from 'node:path';

const QUIET = process.argv.includes('--quiet');
const ROOT = process.cwd();

let findings = [];
let checksRun = 0;

function fail(check, file, message, line) {
  findings.push({ check, file, message, line });
}
function ok(msg) {
  if (!QUIET) console.log(`  \u2713 ${msg}`);
}
function heading(msg) {
  if (!QUIET) console.log(`\n\u2500\u2500 ${msg}`);
}

// ── File discovery ──────────────────────────────────────────────────────────
function walk(dir, predicate, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'obj' || entry.name === 'bin' || entry.name === '.git') continue;
      walk(full, predicate, out);
    } else if (predicate(full)) {
      out.push(full);
    }
  }
  return out;
}

const rel = (p) => path.relative(ROOT, p).split(path.sep).join('/');

// ── VB source cleaning ──────────────────────────────────────────────────────
// Removes comments and blanks out string literals, so that a keyword or quote
// inside a comment or a string cannot be mistaken for code. VB doubles a quote
// to escape it, which is why this scans rather than using a regex.
function cleanLines(source) {
  const lines = source.split(/\r?\n/);
  return lines.map((raw) => {
    let out = '';
    let inString = false;
    for (let i = 0; i < raw.length; i++) {
      const ch = raw[i];
      if (inString) {
        if (ch === '"') {
          if (raw[i + 1] === '"') { i++; continue; }   // escaped quote
          inString = false;
        }
        continue;                                      // drop literal content
      }
      if (ch === '"') { inString = true; continue; }
      if (ch === "'") break;                           // comment to end of line
      out += ch;
    }
    return out;
  });
}

// ── 1. Block balance ────────────────────────────────────────────────────────
// A declaration is `Sub Name` / `Function Name`; a lambda is `Sub(` / `Function(`.
// Only declarations open a block, which is what keeps lambdas from producing
// false positives.
const BLOCK_RULES = [
  { open: /^\s*(?:<[^>]*>\s*)*(?:(?:Public|Private|Protected|Friend|Shared|Partial|NotInheritable|MustInherit|Static)\s+)*Namespace\s+\S/, close: 'End Namespace', label: 'Namespace' },
  { open: /^\s*(?:<[^>]*>\s*)*(?:(?:Public|Private|Protected|Friend|Shared|Partial|NotInheritable|MustInherit)\s+)*Class\s+\S/, close: 'End Class', label: 'Class' },
  { open: /^\s*(?:<[^>]*>\s*)*(?:(?:Public|Private|Protected|Friend)\s+)*Module\s+\S/, close: 'End Module', label: 'Module' },
  { open: /^\s*(?:<[^>]*>\s*)*(?:(?:Public|Private|Protected|Friend)\s+)*Structure\s+\S/, close: 'End Structure', label: 'Structure' },
  { open: /^\s*(?:<[^>]*>\s*)*(?:(?:Public|Private|Protected|Friend)\s+)*Interface\s+\S/, close: 'End Interface', label: 'Interface' },
  { open: /^\s*(?:<[^>]*>\s*)*(?:(?:Public|Private|Protected|Friend)\s+)*Enum\s+\S/, close: 'End Enum', label: 'Enum' },
  { open: /^\s*(?:(?:Public|Private|Protected|Friend|Shared|Overrides|Overridable|NotOverridable|MustOverride|Async|Static|Protected Friend|Private Protected)\s+)*Sub\s+[A-Za-z_\[]/, close: 'End Sub', label: 'Sub' },
  { open: /^\s*(?:(?:Public|Private|Protected|Friend|Shared|Overrides|Overridable|NotOverridable|MustOverride|Async|Static|Protected Friend|Private Protected)\s+)*Function\s+[A-Za-z_\[]/, close: 'End Function', label: 'Function' },
  { open: /^\s*(?:(?:Public|Private|Protected|Friend|Shared|Overrides|Overridable|NotOverridable|MustOverride|Default|ReadOnly|WriteOnly|Async|Static)\s+)*Property\s+[A-Za-z_\[]/, close: 'End Property', label: 'Property' },
  { open: /^\s*Get\s*$/, close: 'End Get', label: 'Get' },
  { open: /^\s*Set\s*$/, close: 'End Set', label: 'Set' },
  { open: /^\s*Select\s+Case\b/, close: 'End Select', label: 'Select' },
  { open: /^\s*While\b/, close: 'End While', label: 'While' },
  { open: /^\s*For\s+(?:Each\s+)?\S/, close: 'Next', label: 'For' },
  { open: /^\s*Try\s*$/, close: 'End Try', label: 'Try' },
  { open: /^\s*Using\b/, close: 'End Using', label: 'Using' },
  { open: /^\s*With\b/, close: 'End With', label: 'With' },
  { open: /^\s*SyncLock\b/, close: 'End SyncLock', label: 'SyncLock' },
  { open: /^\s*Do\b/, close: 'Loop', label: 'Do' },
  { open: /^\s*Operator\b/, close: 'End Operator', label: 'Operator' },
];

// `If` opens a block in two shapes, and the single-line form opens none:
//
//   If x Then                 -> block (Then ends the line)
//   If x AndAlso              -> block (Then is on a LATER line; the condition
//      y Then                     is continued without a `_`, which VB allows)
//   If x Then DoSomething()   -> no block
//
// Missing the continuation case desynchronises the stack exactly as badly as
// omitting `End If` from the terminator list does.
// True when a parenthesised group contains a comma at depth 1. `If(a, b, c)`
// always does; `If (x And y) = 0` never does. A bare `If\s*\(` test cannot
// tell those apart and wrongly rejects the second as the ternary operator.
function hasTopLevelComma(group) {
  let depth = 0;
  let inString = false;
  for (let i = 0; i < group.length; i++) {
    const ch = group[i];
    if (inString) {
      if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"') { inString = true; continue; }
    if (ch === '(') depth++;
    else if (ch === ')') depth--;
    else if (ch === ',' && depth === 1) return true;
  }
  return false;
}

function isBlockIf(line) {
  if (!/^\s*If\b/.test(line)) return false;      // excludes ElseIf and #If
  if (/\bThen\s*$/.test(line)) return true;       // Then ends the line

  // Ternary conditional operator, which is an expression and has no End If.
  // Deliberately written WITHOUT a backslash-escaped paren in a pattern: that
  // is precisely where an over-escaped pattern silently stopped matching.
  const trimmedIf = line.trim();
  if (trimmedIf.startsWith('If(') && trimmedIf.endsWith(')')) {
    if (hasTopLevelComma(trimmedIf.slice(2))) return false;
  }

  if (!/\bThen\b/.test(line)) return true;        // Then arrives on a later line
  return false;                                   // single-line If
}

// The next line that is not blank, used to tell an auto-implemented property
// from one with a body.
function nextCodeLine(lines, from) {
  for (let i = from + 1; i < lines.length; i++) {
    if (lines[i].trim().length > 0) return lines[i].trim();
  }
  return '';
}

function checkBlockBalance(file, lines) {
  checksRun++;
  // Pairs of (opener, terminator). Inside a single-line statement no block is
  // opened, so `Next`/`Loop` etc. are matched against the innermost opener.
  const stack = [];
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();
    if (trimmed.length === 0) continue;

    // Terminators are matched FIRST, so `End If` is never read as an opener.
    // `End If` must be present in this list: leaving it out does not merely
    // miss one case, it desynchronises the stack and reports every following
    // block as unbalanced. Longer strings are listed before their prefixes.
    const TERMINATORS = ['End Namespace', 'End Interface', 'End Structure',
      'End Module', 'End Class', 'End Enum', 'End Sub', 'End Function',
      'End Property', 'End Select', 'End SyncLock', 'End Operator',
      'End While', 'End Try', 'End Using', 'End With',
      'End Get', 'End Set', 'End If', 'Next', 'Loop'];

    // `Next` and `Loop` take an optional trailing clause — `Next i`,
    // `Loop While condition`, `Loop Until condition` — so an exact-string
    // comparison misses them and desynchronises the stack.
    const terminator = TERMINATORS.find((t) => {
      if (t === 'Next') return /^Next\b/.test(trimmed);
      if (t === 'Loop') return /^Loop\b/.test(trimmed);
      return trimmed === t || trimmed.startsWith(t);
    });

    if (terminator) {
      const top = stack.pop();
      if (!top) {
        fail('block-balance', file, `stray \`${terminator}\` with no open block`, i + 1);
      } else if (terminator !== top.close) {
        // `Next` closes For and `Loop` closes Do, so a direct comparison is
        // the correct test; treating them as wildcards would hide real errors.
        fail('block-balance', file,
          `\`${terminator}\` closes ${top.label} opened at line ${top.line}, which needs \`${top.close}\``,
          i + 1);
      }
      continue;
    }

    if (isBlockIf(line)) {
      stack.push({ label: 'If', close: 'End If', line: i + 1 });
      continue;
    }

    // Inside an Interface, and for MustOverride/Declare members, Sub/Function/
    // Property are signature-only and have no terminator at all.
    const insideInterface = stack.some((s) => s.label === 'Interface');

    for (const rule of BLOCK_RULES) {
      if (!rule.open.test(line)) continue;

      if (insideInterface &&
          ['Sub', 'Function', 'Property', 'Get', 'Set'].includes(rule.label)) {
        break;                                   // bodyless interface member
      }
      if ((rule.label === 'Sub' || rule.label === 'Function') &&
          /\b(?:MustOverride|Declare)\b/.test(line)) {
        break;                                   // bodyless by design
      }
      if (rule.label === 'Property') {
        // Auto-implemented properties are one line with no End Property:
        //   Public Property Foo As Integer
        // A property with a body is followed by Get/Set/Implements or by
        // End Property directly. Anything else has no block.
        const next = nextCodeLine(lines, i);
        const hasBody = /^(?:Get|Set)\s*$/.test(next) ||
                        /^End Property\s*$/.test(next) ||
                        /^Implements\b/.test(next);
        if (!hasBody) break;
      }

      stack.push({ label: rule.label, close: rule.close, line: i + 1 });
      break;
    }
  }

  for (const unclosed of stack) {
    fail('block-balance', file,
      `${unclosed.label} opened at line ${unclosed.line} is never closed (needs \`${unclosed.close}\`)`,
      unclosed.line);
  }
  if (stack.length === 0) ok(`${rel(file)}: blocks balanced`);
}

// ── 2/5. Namespaces, declarations and cross-project imports ─────────────────
function readProject(file) {
  const xml = fs.readFileSync(file, 'utf8');
  const rootNamespace = (xml.match(/<RootNamespace>([^<]*)</) || [, ''])[1].trim();
  const assemblyName = (xml.match(/<AssemblyName>([^<]*)</) || [, ''])[1].trim();
  // VB projects declare .vb files as <Compile>, but .xaml and .resw are
  // declared as <Page>/<ApplicationDefinition> and <PRIResource>/<Content>.
  // Comparing every file against <Compile> alone produces false alarms for
  // every XAML and resource file, so gather every declared item instead.
  const ITEM_TYPES = ['Compile', 'Page', 'ApplicationDefinition', 'PRIResource',
    'Content', 'EmbeddedResource', 'Resource', 'None', 'DesignTime',
    'DesignTimeSharedInput', 'None'];
  const itemPattern = new RegExp(
    `<(${ITEM_TYPES.join('|')})\\s+Include="([^"]+)"`, 'g');
  const declaredItems = [...xml.matchAll(itemPattern)]
    .map((m) => m[2].replace(/\\/g, '/'));
  const refs = [...xml.matchAll(/<ProjectReference Include="([^"]+)"/g)]
    .map((m) => m[1].replace(/\\/g, '/'));
  return { rootNamespace, assemblyName, declaredItems, refs };
}

const projectFiles = walk(ROOT, (f) => f.endsWith('.vbproj'));
const projects = projectFiles.map((f) => {
  const dir = path.dirname(f);
  return { file: f, dir, name: path.basename(f, '.vbproj'), ...readProject(f) };
});
const projectByName = new Map(projects.map((p) => [p.name, p]));

// Full namespace of a file = RootNamespace + the file's own Namespace block.
function fileNamespaces(source, project) {
  const declared = [...source.matchAll(/^\s*Namespace\s+([\w.]+)\s*$/gm)].map((m) => m[1]);
  if (declared.length === 0) return [project.rootNamespace];
  return declared.map((d) =>
    project.rootNamespace ? `${project.rootNamespace}.${d}` : d);
}

function checkNamespacesAndImports() {
  heading('Namespace resolution and cross-project imports');

  const knownNamespaces = new Set();
  const fileNamespaceMap = new Map();

  for (const project of projects) {
    const sources = walk(project.dir, (f) => f.endsWith('.vb'));
    for (const src of sources) {
      const text = fs.readFileSync(src, 'utf8');
      const namespaces = fileNamespaces(text, project);
      fileNamespaceMap.set(src, namespaces);
      for (const ns of namespaces) knownNamespaces.add(ns);
    }
  }
  checksRun++;

  for (const project of projects) {
    const sources = walk(project.dir, (f) => f.endsWith('.vb'));
    for (const src of sources) {
      const text = fs.readFileSync(src, 'utf8');
      const lines = cleanLines(text);
      lines.forEach((line, idx) => {
        const m = line.match(/^\s*Imports\s+([\w.]+)\s*$/);
        if (!m) return;
        const ns = m[1];
        // Only BrowserForWP namespaces are ours to resolve; System.* etc. are
        // the compiler's business.
        if (!ns.startsWith('BrowserForWP')) return;
        if (!knownNamespaces.has(ns)) {
          const hint = [...knownNamespaces]
            .filter((k) => k.startsWith(ns) || ns.startsWith(k))
            .sort();
          fail('imports', src,
            `Imports ${ns} does not match any namespace in this solution` +
            (hint.length ? ` (did you mean: ${hint.join(', ')}?)` : ''),
            idx + 1);
        }
      });
    }
  }
  if (findings.every((f) => f.check !== 'imports')) {
    ok(`${knownNamespaces.size} namespaces resolve; all BrowserForWP imports match one`);
  }
  return { knownNamespaces, fileNamespaceMap };
}

// ── 3. Project file vs disk parity ──────────────────────────────────────────
function checkProjectParity() {
  heading('Project file / disk parity');
  checksRun++;
  let anyBad = false;

  for (const project of projects) {
    const onDisk = walk(project.dir, (f) => f.endsWith('.vb') || f.endsWith('.resw') || f.endsWith('.xaml'))
      .map((f) => path.relative(project.dir, f).split(path.sep).join('/'))
      .sort();

    // `App.xaml` is deliberately absent from the item list in some templates and
    // is picked up by the SDK targets, so it is not a parity defect.
    const IGNORED = new Set(['App.xaml']);

    const declared = new Set(project.declaredItems);
    const missing = onDisk.filter((f) => !declared.has(f) && !IGNORED.has(f));
    const phantom = project.declaredItems.filter((f) =>
      /(\.vb|\.xaml|\.resw)$/.test(f) && !fs.existsSync(path.join(project.dir, f)));

    for (const f of missing) {
      fail('project-parity', project.file, `file exists on disk but is not in <Compile Include>: ${f}`);
      anyBad = true;
    }
    for (const f of phantom) {
      fail('project-parity', project.file, `<Compile Include> names a file that does not exist: ${f}`);
      anyBad = true;
    }
  }
  if (!anyBad) ok('every .vb/.xaml/.resw is declared, and every declaration exists');
}

// ── 4. Implements completeness ──────────────────────────────────────────────
function checkImplements() {
  heading('Implements completeness');
  checksRun++;
  let anyBad = false;

  for (const project of projects) {
    for (const src of walk(project.dir, (f) => f.endsWith('.vb'))) {
      const text = fs.readFileSync(src, 'utf8');
      const lines = cleanLines(text);

      lines.forEach((line, idx) => {
        const m = line.match(/^\s*(?:(?:Public|Private|Friend|Partial|NotInheritable)\s+)*Class\s+(\w+)[^\n]*\bImplements\s+([\w.,\s]+)$/);
        if (!m) return;
        const classLine = idx;
        const interfaces = m[2].split(',').map((s) => s.trim()).filter(Boolean);

        // Scan forward to this class's `End Class` to collect implemented members.
        const implemented = new Set();
        for (let i = classLine + 1; i < lines.length; i++) {
          if (/^\s*End Class\s*$/.test(lines[i])) break;
          const impl = lines[i].match(/\bImplements\s+([\w.]+)\s*$/);
          if (impl) implemented.add(impl[1]);
        }

        for (const iface of interfaces) {
          const short = iface.split('.').pop();
          const members = IMPLEMENTED_MEMBERS_BY_INTERFACE[short];
          if (!members) continue;      // not a framework interface we know
          for (const member of members) {
            if (!implemented.has(`${iface}.${member}`) && !implemented.has(member)) {
              fail('implements', src,
                `class ${m[1]} declares Implements ${iface} but never implements ${member}`,
                classLine + 1);
              anyBad = true;
            }
          }
        }
      });
    }
  }
  if (!anyBad) ok('every declared framework interface has its members implemented');
}

// Only interfaces with an exactly-known member set can be verified this way.
// Adding an entry is how you extend this check; guessing is worse than skipping.
const IMPLEMENTED_MEMBERS_BY_INTERFACE = {
  IDisposable: ['Dispose'],
};

// ── 6. Resource parity ─────────────────────────────────────────────────────
function checkResourceParity() {
  heading('Localization resource parity');
  checksRun++;

  const reswFiles = walk(ROOT, (f) => f.endsWith('Resources.resw'));
  if (reswFiles.length === 0) {
    fail('resw', '(project)', 'no Resources.resw files found');
    return;
  }

  const byLanguage = new Map();
  for (const file of reswFiles) {
    const lang = path.basename(path.dirname(file));
    const xml = fs.readFileSync(file, 'utf8');
    const keys = [...xml.matchAll(/<data\s+name="([^"]+)"/g)].map((m) => m[1]).sort();
    byLanguage.set(lang, { keys, file });
  }

  const languages = [...byLanguage.keys()].sort();
  const reference = byLanguage.get(languages[0]);

  let anyBad = false;
  for (const lang of languages.slice(1)) {
    const current = byLanguage.get(lang);
    const missing = reference.keys.filter((k) => !current.keys.includes(k));
    const extra = current.keys.filter((k) => !reference.keys.includes(k));
    for (const k of missing) {
      fail('resw', current.file, `key "${k}" present in ${languages[0]} but missing here`);
      anyBad = true;
    }
    for (const k of extra) {
      fail('resw', current.file, `key "${k}" is here but missing in ${languages[0]}`);
      anyBad = true;
    }
  }
  if (!anyBad) {
    ok(`${languages.join(' / ')} define identical key sets (${reference.keys.length} keys each)`);
  }
}

// ── 7. XAML handler wiring ─────────────────────────────────────────────────
function checkXamlHandlers() {
  heading('XAML event handler wiring');
  checksRun++;
  let anyBad = false;

  const xamlFiles = walk(ROOT, (f) => f.endsWith('.xaml'));
  for (const xaml of xamlFiles) {
    const codeBehind = `${xaml}.vb`;
    if (!fs.existsSync(codeBehind)) continue;

    const xamlText = fs.readFileSync(xaml, 'utf8');
    const handlers = [...xamlText.matchAll(/\b(?:Click|Tapped|SelectionChanged|Checked|Unchecked|Loaded|TextChanged|KeyDown|LostFocus|GotFocus|SizeChanged|ValueChanged|Completed|Holding|PointerPressed|PointerReleased)="([A-Za-z_]\w*)"/g)]
      .map((m) => m[1]);
    if (handlers.length === 0) continue;

    const vbText = fs.readFileSync(codeBehind, 'utf8');
    for (const handler of new Set(handlers)) {
      const declared = new RegExp(
        String.raw`^\s*(?:(?:Public|Private|Protected|Friend|Shared|Async)\s+)*Sub\s+${handler}\s*\(`,
        'm');
      if (!declared.test(vbText)) {
        fail('xaml-handler', xaml, `event handler "${handler}" is referenced but not defined in ${path.basename(codeBehind)}`);
        anyBad = true;
      }
    }
  }
  if (!anyBad) ok('every event handler named in XAML is defined in its code-behind');
}

// ── Run ────────────────────────────────────────────────────────────────────
console.log('VB.NET structural checker — BrowserForWP');
console.log('(This is NOT a compiler. See the header for exactly what it proves.)');

heading('Block balance');
for (const project of projects) {
  for (const src of walk(project.dir, (f) => f.endsWith('.vb'))) {
    checkBlockBalance(src, cleanLines(fs.readFileSync(src, 'utf8')));
  }
}

checkProjectParity();
checkNamespacesAndImports();
checkImplements();
checkResourceParity();
checkXamlHandlers();

console.log(`\n${checksRun} check group(s) run, ${findings.length} finding(s).`);
if (findings.length > 0) {
  console.log('\nFindings:');
  for (const f of findings) {
    console.log(`  [${f.check}] ${rel(f.file)}${f.line ? ':' + f.line : ''}`);
    console.log(`      ${f.message}`);
  }
  process.exit(1);
}
console.log('\nNo mechanical defects found in the seven checked categories.');
console.log('This does NOT mean the project compiles — build it on x64 Windows');
console.log('with Visual Studio 2013 Update 4 and the Windows Phone 8.1 SDK.');
