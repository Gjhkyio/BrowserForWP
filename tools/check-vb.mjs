#!/usr/bin/env node
// ═══════════════════════════════════════════════════════════════════════════
//  Static VB.NET structural checker — BrowserForWP
//
//  WHY THIS EXISTS
//  ---------------
//  The Windows Phone 8.1 SDK cannot be installed on this project's development
//  host (an Apple silicon Mac), so the sources are developed off-platform.
//
//  They are NOT, however, uncompiled. An ARM64 Windows 11 Parallels guest does
//  host the whole VS2013 toolchain and builds the solution for real; see
//  tools/vm-build.cmd and the round-by-round transcripts in docs/MAINTAINING.md.
//  An earlier revision of this header claimed the VM could not host VS2013. That
//  was wrong, and it is worth saying so plainly: the claim was inferred from
//  Microsoft's "Visual Studio does not support Arm processors" documentation
//  instead of from trying it, and the cost of not trying was three rounds of
//  compile errors that a two-minute build would have surfaced immediately.
//
//  This tool still earns its place, because the guest round trip is slow and its
//  output is in Italian. It catches the cheap, repetitive mistakes first, on any
//  machine:
//
//    1. Block balance          every Class/Sub/If/Try/... has its terminator
//    2. Implements completeness an `Implements IDisposable` needs matching members
//    3. Project/disk parity    every .vb on disk is in the .vbproj and vice versa
//    4. Namespace resolution   RootNamespace + declared Namespace actually produces
//                              the namespace that other projects import
//    5. Cross-project imports  every `Imports BrowserForWP.X` names a real namespace
//    6. Resource parity        en-US and it-IT define exactly the same keys
//    7. XAML handler wiring    every event handler named in XAML exists in VB
//    8. XAML single root child  a Page sets Content exactly once
//    9. Theme resources        every `{ThemeResource X}` in XAML exists on WP8.1
//   10. Char-range literals    ChrW cannot express a supplementary-plane code point
//   11. VB 12 syntax           no leading-dot line continuation (VS2015 and later)
//   12. Profile hazards        APIs absent from .NET for Windows Store apps
//   13. Comment hazards        '--' in XML comments, unescaped '<' in doc comments
//   14. Project flavour        the flavour GUID the IDE uses to resolve references
//
//  WHAT IT CANNOT DO
//  -----------------
//  It is not a compiler. It cannot type-check, resolve overloads, verify WinRT
//  API availability in general, or catch a wrong method signature. A clean run
//  means "no mechanical defects of these kinds", not "compiles".
//
//  Concretely, every one of the following compiled clean here and failed in the
//  guest, so treat a green run as a filter and not as a verdict:
//
//    * `Friend` members used from a referencing assembly (BC30390)
//    * a nested class named in another file's signature (BC30002)
//    * a local named the same as an enclosing type or member, which VB's
//      case-insensitivity turns into a shadowing error at the USE site
//      (BC30039 / BC30456 "is not a member of 'Integer'")
//
//  A XAML `{ThemeResource ...}` key that does not exist on WP8.1 was on this list
//  until it cost a round: `MainPage.xaml` asked for `TextControlBackground`, a
//  UWP-era name the phone does not define, and shipped. Group 9 now checks it
//  against the phone's own dictionaries, so it is no longer uncatchable.
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
  // Strip XML comments BEFORE scanning for items. Without this, an `<Item>`
  // example written inside a comment is parsed as a real declaration -- which is
  // exactly what happened: a comment describing the old
  // `<Content Include="Polyfill\compat.js" />` was read as a live item and
  // reported as a missing file. The same mistake the .vb scanner already avoids.
  const xml = fs.readFileSync(file, 'utf8').replace(/<!--[\s\S]*?-->/g, '');
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
  // The project FLAVOUR and the target platform are two separate statements, and
  // a project can make them disagree without either looking wrong on its own.
  // See checkProjectFlavor().
  const projectTypeGuids = (xml.match(/<ProjectTypeGuids>([^<]*)</) || [, ''])[1].trim();
  const targetPlatformIdentifier = [...xml.matchAll(/<TargetPlatformIdentifier>([^<]*)</g)]
    .map((m) => m[1].trim());
  return { rootNamespace, assemblyName, declaredItems, refs, projectTypeGuids,
    targetPlatformIdentifier };
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

    // No exceptions. An earlier version of this tool ignored App.xaml, which was
    // wrong: App.xaml belongs in <ApplicationDefinition>, and a missing one means
    // no generated entry point at all. Silencing that finding hid nothing here,
    // but the habit is what hides the next real defect.
    const declared = new Set(project.declaredItems);
    const missing = onDisk.filter((f) => !declared.has(f));

    // EVERY declared item must exist, not just source files. An earlier version
    // of this check only covered .vb/.xaml/.resw, and that hole let through a
    // `<Content Include="Polyfill\compat.js">` pointing at a file that was never
    // created -- a defect that fails the build with an unattributed "cannot find
    // the path specified" and then cascades into dozens of misleading errors.
    // Do not narrow this again.
    const phantom = project.declaredItems.filter((f) => {
      if (f.includes('*') || f.includes('?')) return false;   // MSBuild wildcard
      // A Link item is resolved by MSBuild relative to the project; the Include
      // path may legitimately point outside the project directory.
      return !fs.existsSync(path.join(project.dir, f));
    });

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

// ── 14. Project flavour (Windows Phone 8.1 vs Windows Store) ────────────────
// The first GUID in ProjectTypeGuids is the project FLAVOUR. It is read by the
// IDE's project system, not by MSBuild, and a Windows Phone 8.1 app may only
// resolve references to projects of the same flavour.
//
// This solution's libraries carried {BC8A1FFA-...}, the WINDOWS STORE flavour,
// while declaring TargetPlatformIdentifier WindowsPhoneApp. Each file therefore
// looked right on its own, and the guest build stayed green: MSBuild compares
// neither GUID. The IDE, which does, reported
//
//     The referenced component 'BrowserForWP.Core' could not be found.
//
// once per reference, naming projects that were present, correct and already
// built. Two things identify the message as the project system's and not the
// compiler's: it carries no diagnostic code (every BC and MSB failure does), and
// the English string occurs nowhere under MSBuild, the Windows Phone 8.1 SDK or
// the Windows Kits on the guest -- so no build here can reproduce or refute it.
// That is also why tools/vm-build.cmd never saw it.
//
// The authoritative values are the VS2013 templates on the guest:
//
//   ProjectTemplates\VisualBasic\Windows Phone 8.1\1033\
//       WindowsPhoneClassLibrary\ClassLibrary.vbproj      {76F1466A-...}
//   ProjectTemplates\VisualBasic\Windows Phone 8.1\1033\
//       WindowsPhoneBlankApplication\Application.vbproj    {76F1466A-...}
//   ProjectTemplates\VisualBasic\Windows Store\1033\
//       ClassLibrary_WindowsStoreApps\ClassLibrary.vbproj  {BC8A1FFA-...}
//
// The app template and the Windows Phone 8.1 class library template agree, which
// is the whole rule: a WindowsPhoneApp project uses the 76F1466A flavour.
//
// The .sln records a project type GUID per project as well, and it is checked too
// so the two files cannot tell different stories. It is a hint rather than the
// deciding field: in this VS2013 installation neither the phone nor the Store
// flavour GUID is registered as a project factory under
//
//   HKLM\SOFTWARE[\WOW6432Node]\Microsoft\VisualStudio\12.0\Projects
//
// where only {F184B08F-C81C-45F6-A57F-5ABD9991F28F} (VB) appears, so the loader
// resolves these projects through the project file. That is also why no MSBuild
// build can see any of it.

const WP81_FLAVOR_GUID = '{76F1466A-8B6D-4E39-A767-685A06062A39}';
const WINDOWS_STORE_FLAVOR_GUID = '{BC8A1FFA-BEE3-4634-8014-F334798102B3}';

function checkProjectFlavor() {
  heading('Project flavour (WindowsPhoneApp vs Windows Store)');
  checksRun++;
  let anyBad = false;

  for (const project of projects) {
    const guids = project.projectTypeGuids.toUpperCase();
    const phoneTarget = project.targetPlatformIdentifier.includes('WindowsPhoneApp');

    if (guids.includes(WINDOWS_STORE_FLAVOR_GUID)) {
      const also = phoneTarget
        ? ' This project also declares TargetPlatformIdentifier WindowsPhoneApp, so the ' +
          'two statements contradict each other.'
        : '';
      fail('project-flavor', project.file,
        `ProjectTypeGuids carries the Windows Store flavour ${WINDOWS_STORE_FLAVOR_GUID}.` +
        also +
        ' The IDE reads the flavour, so a Windows Phone 8.1 app cannot resolve a reference ' +
        'to this project: its error list says the referenced component could not be found, ' +
        'naming a project that is present, correct and already built. No build catches this ' +
        `because MSBuild never reads ProjectTypeGuids. Use ${WP81_FLAVOR_GUID}, the value the ` +
        'Windows Phone 8.1 class library template writes.');
      anyBad = true;
    } else if (phoneTarget && !guids.includes(WP81_FLAVOR_GUID)) {
      fail('project-flavor', project.file,
        'declares TargetPlatformIdentifier WindowsPhoneApp but does not carry the Windows ' +
        `Phone 8.1 flavour GUID ${WP81_FLAVOR_GUID} in ProjectTypeGuids. The IDE keys on the ` +
        'flavour GUID, so the app cannot resolve this project as a reference.');
      anyBad = true;
    }
  }
  // The same statement in the solution file. See the comment above: the .sln
  // GUID is a hint, but a .sln that says Windows Store for a project whose
  // .vbproj now says Windows Phone is a trap for the next reader.
  const slnFile = path.join(ROOT, 'BrowserForWP.sln');
  if (fs.existsSync(slnFile)) {
    const byPath = new Map(projects.map((p) => [rel(p.file), p]));
    fs.readFileSync(slnFile, 'utf8').split(/\r?\n/).forEach((raw, idx) => {
      const m = /^Project\("\{([^"]+)\}"\) = "([^"]+)", "([^"]+)", "\{([^"]+)\}"/.exec(raw);
      if (!m) return;                                     // not a project line
      const declaredPath = m[3].replace(/\\/g, '/');
      if (!declaredPath.endsWith('.vbproj')) return;      // solution folders
      const project = byPath.get(declaredPath);
      if (project === undefined) return;                  // reported by group 3
      const slnGuid = `{${m[1].toUpperCase()}}`;
      if (slnGuid !== WP81_FLAVOR_GUID) {
        fail('project-flavor', slnFile,
          `the solution declares ${m[2]} with project type ${slnGuid}, while the project ` +
          `carries ${project.projectTypeGuids}. Give both the Windows Phone 8.1 flavour ` +
          `${WP81_FLAVOR_GUID}: a solution that says Windows Store for a Windows Phone project ` +
          'invites the next reader to "fix" the project file the wrong way, which is how the ' +
          'four reference warnings were introduced.', idx + 1);
        anyBad = true;
      }
    });
  }
  if (!anyBad) ok('every project, in the .vbproj and in the .sln, carries the Windows Phone 8.1 flavour GUID');
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

// ── 8. XAML single root child ──────────────────────────────────────────────
// A ContentControl (Page, UserControl, Window) can have exactly ONE Content.
// Two direct children is the error "The property 'Content' can only be set
// once", and the damage is much larger than the message suggests: the XAML
// compiler rejects the file, so the generated .g.vb is never produced, so every
// x:Name field becomes "not declared" in the code-behind and the project loses
// its generated entry point. One structural mistake, dozens of errors — which is
// exactly why it is worth a dedicated check.
function countRootChildren(xml, rootTag) {
  const tagPattern = /<(\/?)([A-Za-z][\w.:]*)((?:"[^"]*"|'[^']*'|[^>])*?)(\/?)>/g;
  let depth = 0;
  let children = 0;
  let match;
  let seenRoot = false;
  while ((match = tagPattern.exec(xml)) !== null) {
    const isClosing = match[1] === '/';
    const name = match[2];
    const isSelfClosing = match[4] === '/';

    if (isClosing) { depth--; continue; }
    if (!seenRoot) { seenRoot = true; depth = 1; continue; }   // the root itself
    if (isSelfClosing) {
      if (depth === 1) children++;
      continue;
    }
    if (depth === 1) children++;
    depth++;
  }
  return children;
}

function checkXamlRoot() {
  heading('XAML single root child');
  checksRun++;
  let anyBad = false;

  for (const xaml of walk(ROOT, (f) => f.endsWith('.xaml'))) {
    const body = fs.readFileSync(xaml, 'utf8').replace(/<!--[\s\S]*?-->/g, '');
    const root = /<([A-Za-z][\w.:]*)/.exec(body);
    if (!root) continue;

    const children = countRootChildren(body, root[1]);
    if (children > 1) {
      fail('xaml-root', xaml,
        `root <${root[1]}> has ${children} direct children; a ContentControl can hold only one. ` +
        'Wrap them in a single container — otherwise the XAML compiler rejects the file, the ' +
        'generated .g.vb is never created, and every x:Name field becomes "not declared".');
      anyBad = true;
    }
  }
  if (!anyBad) ok('every XAML file has exactly one root child');
}

// ── 9. XAML theme-resource keys ───────────────────────────────────────────
// A `{ThemeResource X}` is resolved when the page LOADS, not when it compiles,
// so an unknown X is not an error that any build here can report. Nothing in the
// toolchain cross-checks the key against the platform. The XAML designer does,
// and names it -- "The resource "X" could not be resolved" -- but a design-time
// error list is a per-machine window and not part of the build.
//
// That is exactly how MainPage.xaml came to ask for `TextControlBackground`, a
// UWP-era name Windows Phone 8.1 does not define, and ship. docs/MAINTAINING.md's
// Round 4 record had put that key there *deliberately*, because a session in
// which WMC9999 came and went made the swap look like the thing that silenced it.
// It was not: the diagnostic is independent of the key. It was present in 12 of 12
// recorded probe runs *with* that key in the file, and it is still present now
// that the key is gone. So the key needed an oracle of its own, not a diagnostic
// to interpret.
//
// The oracle is tools/wp81-theme-keys.txt, the 523 keys Windows Phone 8.1 itself
// defines, regenerated by tools/wp81-theme-keys.sh. It is the PHONE's dictionary
// and not the desktop's: Windows 8.1 ships a themeresources.xaml and a
// generic.xaml too, the two sets differ, and a desktop-only key compiles,
// packages, and then cannot resolve on the handset.
//
// `{StaticResource X}` is deliberately not checked: the markup compiler resolves
// it at compile time, so a missing key is already a build error, not a silent one.

const WP81_THEME_KEYS_FILE = path.join(ROOT, 'tools', 'wp81-theme-keys.txt');

function loadWp81ThemeKeys() {
  if (!fs.existsSync(WP81_THEME_KEYS_FILE)) return null;
  const keys = new Set();
  for (const raw of fs.readFileSync(WP81_THEME_KEYS_FILE, 'utf8').split(/\r?\n/)) {
    const key = raw.trim();
    if (key !== '' && !key.startsWith('#')) keys.add(key);
  }
  return keys.size > 0 ? keys : null;
}

function checkXamlThemeResources() {
  heading('XAML theme-resource keys (WP8.1)');
  checksRun++;
  let anyBad = false;

  const platform = loadWp81ThemeKeys();
  if (platform === null) {
    fail('theme-resource', WP81_THEME_KEYS_FILE,
      'the WP8.1 theme-resource key list is missing or empty, so this group cannot ' +
      'run at all. Regenerate it on the host with:  bash tools/wp81-theme-keys.sh');
    return;
  }

  for (const xaml of walk(ROOT, (f) => f.endsWith('.xaml'))) {
    // A key the app declares itself is legal, ThemeResource or not.
    const body = fs.readFileSync(xaml, 'utf8').replace(/<!--[\s\S]*?-->/g, '');
    const appDefined = new Set();
    for (const m of body.matchAll(/x:Key="([^"]+)"/g)) appDefined.add(m[1]);

    body.split(/\r?\n/).forEach((raw, idx) => {
      for (const m of raw.matchAll(/\{ThemeResource\s+([A-Za-z_][\w.]*)\s*\}/g)) {
        const key = m[1];
        if (platform.has(key) || appDefined.has(key)) continue;
        fail('theme-resource', xaml,
          `{ThemeResource ${key}} -- Windows Phone 8.1 does not define this key. ` +
          'ThemeResource is resolved at page load, so no build here can reject it and the ' +
          'reference silently cannot resolve on the handset. Use a key from ' +
          'tools/wp81-theme-keys.txt, or declare your own in App.xaml. A Windows 8.1 or ' +
          'UWP name is a different set and will not resolve on this platform.',
          idx + 1);
        anyBad = true;
      }
    });
  }
  if (!anyBad) ok('every {ThemeResource} key exists in the WP8.1 dictionaries');
}

// ── 10. Char-range literals ───────────────────────────────────────────────
// ChrW and Chr return a Char, which is 16 bits. A supplementary-plane code point
// such as U+1F512 (128274) does not fit, and the compiler says so in a way that
// is easy to misread: "Value '128274' cannot be converted to 'Char'". The fix is
// Char.ConvertFromUtf32, which returns a String.
function checkCharLiterals() {
  heading('Char-range literals');
  checksRun++;
  let anyBad = false;

  for (const project of projects) {
    for (const src of walk(project.dir, (f) => f.endsWith('.vb'))) {
      const lines = cleanLines(fs.readFileSync(src, 'utf8'));
      lines.forEach((line, idx) => {
        const pattern = /\bChrW?\s*\(\s*(?:&H([0-9A-Fa-f]+)|(\d+))\s*\)/g;
        let match;
        while ((match = pattern.exec(line)) !== null) {
          const value = match[1] ? parseInt(match[1], 16) : parseInt(match[2], 10);
          if (value > 0xffff) {
            fail('char-range', src,
              `${match[0]} is ${value}, above &HFFFF. A Char is 16 bits, so this cannot compile. ` +
              'Use Char.ConvertFromUtf32(...) — but check the handset fonts cover the glyph.',
              idx + 1);
            anyBad = true;
          }
        }
      });
    }
  }
  if (!anyBad) ok('no ChrW/Chr call exceeds the 16-bit range of a Char');
}

// ── 11. VB 12 syntax ──────────────────────────────────────────────────────
// Visual Studio 2013 ships VB 12. Implicit line continuation AFTER a '.'
// arrived in VB 14 (VS2015), so the JavaScript-style fluent chain
//
//     Dim w = New TlsWriter().
//         U8(1).
//         U16(2).
//         ToArray()
//
// is not merely unconventional here, it does not parse. The damage is worse
// than one bad line: the compiler reports BC30203 on the trailing dot and then
// "<name> is not declared" for every method in the chain, so a 4-line mistake
// produces twenty errors that all name the wrong thing. Rewrite as a `With`
// block, which reads the same and compiles.
function checkVb12Syntax() {
  heading('VB 12 syntax (no VS2015-only constructs)');
  checksRun++;
  let anyBad = false;

  for (const project of projects) {
    for (const src of walk(project.dir, (f) => f.endsWith('.vb'))) {
      const lines = cleanLines(fs.readFileSync(src, 'utf8'));
      lines.forEach((line, idx) => {
        // A code line ending in '.' is a chain continued on the next line.
        // Comments and string contents are already gone, so a sentence in a
        // comment or a '.' inside a literal cannot trigger this.
        if (/\.\s*$/.test(line)) {
          fail('vb12', src,
            'line ends with ".", so the next line continues a method chain. VB 12 ' +
            '(VS2013) has no implicit line continuation after a period; this is BC30203 ' +
            'plus a phantom "not declared" for every following method. Use a With block ' +
            'and one call per line.',
            idx + 1);
          anyBad = true;
        }
      });
    }
  }
  if (!anyBad) ok('no leading-dot method chains (VB 12 cannot continue a line after ".")');
}

// ── 12. Fine .NET-for-Windows-Store-apps profile hazards ──────────────────
// The WP8.1 app and library projects compile against the .NET for Windows
// Store apps profile, not full .NET. A handful of members that are unconditional
// on the desktop simply are not there, and the compiler error names the member
// as missing from a type that obviously has it, which reads like a typo.
const PROFILE_HAZARDS = [
  [/\bEncoding\.(ASCII|UTF7|UTF32)\b/,
    'not in the Store profile (it needs ASCIIEncoding/UTF7Encoding, which the ' +
    'profile removes). Encode ASCII explicitly, or use Encoding.UTF8 after ' +
    'validating the input is ASCII.'],
  [/\bRegexOptions\.Compiled\b/,
    'RegexOptions.Compiled is not supported in the Store profile.'],
  [/\b(CryptographicException|RNGCryptoServiceProvider|RandomNumberGenerator|SHA256Managed|HMACSHA256|SHA256|HMACSHA1)\b/,
    'System.Security.Cryptography is not in the Store profile. Use ' +
    'Windows.Security.Cryptography.Core through WinRtCrypto, and InvalidOperationException ' +
    'or ArgumentException for failures.'],
  [/\bCryptographicEngine\.Verify\b/,
    'the WinRT type exposes VerifySignature / VerifySignatureWithHashInput, not Verify.'],
  [/\bControlChars\b/,
    'Microsoft.VisualBasic.ControlChars is not in the Store profile (BC30451), even ' +
    'though Microsoft.VisualBasic.Strings (AscW, ChrW) is. Test whitespace with ' +
    'Char.IsWhiteSpace, or use the numeric Char code.'],
];

function checkProfileHazards() {
  heading('NETFX_CORE profile hazards');
  checksRun++;
  let anyBad = false;

  for (const project of projects) {
    for (const src of walk(project.dir, (f) => f.endsWith('.vb'))) {
      const lines = cleanLines(fs.readFileSync(src, 'utf8'));
      lines.forEach((line, idx) => {
        for (const [pattern, message] of PROFILE_HAZARDS) {
          if (pattern.test(line)) {
            fail('profile', src, `${line.trim()} — ${message}`, idx + 1);
            anyBad = true;
          }
        }
      });
    }
  }
  if (!anyBad) ok('no use of APIs missing from the .NET for Windows Store apps profile');
}

// ── 13. Comment hazards ──────────────────────────────────────────────────
// Two ways a comment can break a build.
//
//  * '--' is illegal inside an XML comment. The project file then fails to
//    load at all (MSB4025), and in a solution build the symptom is the far less
//    informative MSB4078 "project file is not supported by MSBuild". The
//    temptation is to draw a rule line out of dashes in a header comment.
//
//  * An XML doc comment is parsed as XML. `<0..2^24-1>`, copied straight from
//    an RFC's grammar, is an invalid tag name and the whole doc comment is
//    discarded with a warning (BC42304). Escape it as &lt;...&gt;.
const XML_ISH = ['.vbproj', '.xaml', '.appxmanifest', '.resw', '.sln', '.xml'];

function checkCommentHazards() {
  heading('Comment hazards (XML comments, doc comments)');
  checksRun++;
  let anyBad = false;

  // Doubled dashes inside an XML comment, in any XML-ish file.
  for (const file of walk(ROOT, (f) => XML_ISH.some((e) => f.endsWith(e)))) {
    const source = fs.readFileSync(file, 'utf8');
    const lines = source.split(/\r?\n/);
    let inComment = false;
    lines.forEach((raw, idx) => {
      let cursor = 0;
      for (;;) {
        if (!inComment) {
          const open = raw.indexOf('<!--', cursor);
          if (open === -1) break;
          inComment = true;
          cursor = open + 4;
        }
        const close = raw.indexOf('-->', cursor);
        const bodyEnd = close === -1 ? raw.length : close;
        if (raw.slice(cursor, bodyEnd).includes('--')) {
          fail('comment', file,
            'XML comments cannot contain "--". MSBuild refuses to load the whole ' +
            'project (MSB4025); from a solution build this surfaces as the ' +
            'unrelated-looking MSB4078 "project file is not supported by MSBuild".',
            idx + 1);
          anyBad = true;
        }
        if (close === -1) break;
        inComment = false;
        cursor = close + 3;
      }
    });
  }

  // Unescaped '<' followed by a digit inside a ''' doc comment.
  for (const project of projects) {
    for (const src of walk(project.dir, (f) => f.endsWith('.vb'))) {
      fs.readFileSync(src, 'utf8').split(/\r?\n/).forEach((raw, idx) => {
        if (!/^\s*'''/.test(raw)) return;
        if (/<'?\d|<\d/.test(raw)) {
          fail('comment', src,
            'a doc comment is parsed as XML: "<0..2^24-1>" is an invalid tag name ' +
            'and discards the comment with BC42304. Escape it as &lt;...&gt;.',
            idx + 1);
          anyBad = true;
        }
      });
    }
  }

  if (!anyBad) ok('no "--" in XML comments and no unescaped "<" in doc comments');
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
checkProjectFlavor();
checkNamespacesAndImports();
checkImplements();
checkResourceParity();
checkXamlHandlers();
checkXamlRoot();
checkXamlThemeResources();
checkCharLiterals();
checkVb12Syntax();
checkProfileHazards();
checkCommentHazards();

console.log(`\n${checksRun} check group(s) run, ${findings.length} finding(s).`);
if (findings.length > 0) {
  console.log('\nFindings:');
  for (const f of findings) {
    console.log(`  [${f.check}] ${rel(f.file)}${f.line ? ':' + f.line : ''}`);
    console.log(`      ${f.message}`);
  }
  process.exit(1);
}
console.log('\nNo mechanical defects found in the fourteen checked categories.');
console.log('This still does NOT mean the project compiles. Build it for real:');
console.log('');
console.log('  prlctl exec "{66a2f493-162c-4b3f-ba40-0a26020cc818}" "cmd.exe" "/c" \\');
console.log('      "C:\\Mac\\Home\\Documents\\BrowserForWP\\tools\\vm-build.cmd"');
console.log('');
console.log('See docs/MAINTAINING.md for the toolchain layout in that guest.');
