#!/usr/bin/env node
// The executable referee for BrowserForWP.Core/Engine/Remote/RemoteProtocol.vb.
//
// The protocol has two implementations in two languages, and the one on the
// device cannot be run here. So this file does what tools/proto/core-logic.mjs
// does and two things more:
//
//   * It rebuilds the message bytes here and compares them with
//     protocol/vectors.json, which the SERVER's own code produced. A
//     transliteration checked only against itself agrees with itself while
//     disagreeing with the device, which is exactly the failure this catches.
//   * It reads the field order out of the VB source and asserts it against the
//     server's encoders. A field added on one side and not the other is
//     invisible on the wire and a garbled screen on a phone.
//
// The vectors are vendored from Docker-BrowserForWP/protocol/vectors.json. When
// the server regenerates them, replace this copy in the same commit, or this
// referee passes while the device is wrong.
import fs from 'node:fs';

let checks = 0;
let failures = 0;
function check(name, ok, detail = '') {
  checks += 1;
  if (ok) {
    console.log(`  ✓ ${name}`);
  } else {
    console.log(`  ✗ ${name}${detail ? ': ' + detail : ''}`);
    failures++;
  }
}

const SOURCE = 'BrowserForWP.Core/Engine/Remote/RemoteProtocol.vb';
const VECTORS_PATH = 'protocol/vectors.json';

if (!fs.existsSync(VECTORS_PATH)) {
  console.log(`  ✗ ${VECTORS_PATH} is missing. Copy it from the server repository.`);
  process.exit(1);
}
const VECTORS = JSON.parse(fs.readFileSync(VECTORS_PATH, 'utf8'));
const source = fs.existsSync(SOURCE) ? fs.readFileSync(SOURCE, 'utf8') : '';

/**
 * The source with comments and string literals blanked out.
 *
 * Every check below reads THIS, not the raw text, and the reason is a scar this
 * repository already has: a checker that read comments once ended up forbidding
 * the documentation of the very rule it enforced. The file above says in a
 * comment that it does not use BitConverter because BitConverter is
 * little-endian -- so a check for BitConverter that read comments would fail on
 * the sentence explaining why there is none.
 *
 * Characters are replaced rather than removed, so a reported offset still points
 * at the right place in the original file.
 */
function codeOnly(text) {
  const out = [];
  for (const raw of text.split(/\r?\n/)) {
    let line = '';
    let inString = false;
    for (let index = 0; index < raw.length; index += 1) {
      const ch = raw[index];
      if (ch === '"') {
        if (inString && raw[index + 1] === '"') {
          index += 1;
          line += '  ';
          continue;
        }
        inString = !inString;
        line += ' ';
        continue;
      }
      if (!inString && ch === "'") break;
      line += inString ? ' ' : ch;
    }
    out.push(line);
  }
  return out.join('\n');
}

const code = codeOnly(source);

// ── The transliterated writer, byte for byte ────────────────────────────────
// Written out rather than imported, so this file is a statement of the format
// rather than a re-use of the code that produces it.
class Writer {
  constructor() { this.parts = []; }
  u8(v) { this.parts.push(Buffer.from([v & 0xff])); return this; }
  u16(v) { const b = Buffer.alloc(2); b.writeUInt16BE(v & 0xffff, 0); this.parts.push(b); return this; }
  u32(v) { const b = Buffer.alloc(4); b.writeUInt32BE(v >>> 0, 0); this.parts.push(b); return this; }
  i16(v) { const b = Buffer.alloc(2); b.writeInt16BE(v, 0); this.parts.push(b); return this; }
  blob(bytes) { this.u32(bytes.length); this.parts.push(bytes); return this; }
  str(text) { return this.blob(Buffer.from(text, 'utf8')); }
  build() { return Buffer.concat(this.parts); }
}

// ── The constants ───────────────────────────────────────────────────────────
check('the magic in the VB source is the magic in the vectors',
  new RegExp(`Magic As UShort = &H${VECTORS.magicHex.toUpperCase()}\\b`).test(code),
  VECTORS.magicHex);
check('the header size in the VB source is the header size in the vectors',
  new RegExp(`HeaderSize As Integer = ${VECTORS.headerSize}\\b`).test(code),
  String(VECTORS.headerSize));
check('the version in the VB source is the version in the vectors',
  new RegExp(`Version As Byte = ${VECTORS.protocolVersion}\\b`).test(code));
check('sealing is decided by the type, at the threshold the server uses',
  /SealedFrom As Byte = &H10/.test(code));
check('the payload limit is the server\'s, so an oversized frame is refused alike',
  /MaxPayload As Integer = 8 \* 1024 \* 1024/.test(code));

// BitConverter is little-endian and unguarded. Using it would produce a protocol
// that works on x86 by accident and fails on the handset, which is where nobody
// is looking. Checked against the code, not the comments: the file documents
// this rule, and a check that read the documentation would forbid it.
check('the VB code does not use BitConverter anywhere',
  !/\bBitConverter\b/.test(code));

// ── The messages the client builds, against the server's own bytes ──────────

const hello = VECTORS.payloads.find((entry) => entry.name === 'HELLO');
const builtHello = new Writer()
  .u8(VECTORS.protocolVersion)
  .str('0f7c1a2b-4d5e-4f60-8a9b-0c1d2e3f4a5b')
  .str(VECTORS.inputs.tokenBase64Url)
  .u16(480)
  .u16(800)
  .u8(2)
  .str('BrowserForWP/0.1 (WindowsPhone8.1)')
  .build();
check('HELLO built here equals the HELLO the server built',
  builtHello.toString('hex') === hello.hex,
  `\n     ours   ${builtHello.toString('hex')}\n     server ${hello.hex}`);

const tap = VECTORS.payloads.find((entry) => entry.name === 'TAP');
const builtTap = new Writer().u16(120).u16(240).u8(1).u8(1).build();
check('TAP built here equals the TAP the server built',
  builtTap.toString('hex') === tap.hex,
  `\n     ours   ${builtTap.toString('hex')}\n     server ${tap.hex}`);

const scroll = VECTORS.payloads.find((entry) => entry.name === 'SCROLL');
const builtScroll = new Writer().u16(200).u16(300).i16(0).i16(-120).build();
check('SCROLL carries its deltas as SIGNED 16-bit values',
  builtScroll.toString('hex') === scroll.hex,
  `\n     ours   ${builtScroll.toString('hex')}\n     server ${scroll.hex}`);

const settings = VECTORS.payloads.find((entry) => entry.name === 'SETTINGS');
let settingsFlags = 0;
if (true) settingsFlags |= 1;   // night mode
if (false) settingsFlags |= 2;  // desktop mode
if (true) settingsFlags |= 4;   // block trackers
const builtSettings = new Writer().u8(settingsFlags).build();
check('SETTINGS packs its three flags the way the server unpacks them',
  builtSettings.toString('hex') === settings.hex,
  `\n     ours   ${builtSettings.toString('hex')}\n     server ${settings.hex}`);

const ack = VECTORS.payloads.find((entry) => entry.name === 'ACK');
check('ACK is a bare u32 sequence number',
  new Writer().u32(7).build().toString('hex') === ack.hex);

// ── The source contract: the field order the VB must have ───────────────────
// Each list is the order the VB encoder must walk, taken from the server's own
// encoder. This is what catches a field added on one side only.
const ORDER = {
  EncodeHello: ['U8', 'Str', 'Str', 'U16', 'U16', 'U8', 'Str'],
  EncodeNavigate: ['Str'],
  EncodeResize: ['U16', 'U16', 'U8'],
  EncodeTap: ['U16', 'U16', 'U8', 'U8'],
  EncodeScroll: ['U16', 'U16', 'I16', 'I16'],
  EncodeKey: ['Str', 'U8', 'Str'],
  EncodeText: ['Str'],
  EncodeFind: ['Str'],
  EncodeSettings: ['U8'],
  EncodeNonce: ['U32'],
  EncodeAck: ['U32'],
  EncodeTitle: ['Str'],
  EncodeLoadState: ['U8', 'Str'],
  EncodeFindResult: ['U8', 'U32'],
  EncodeAudio: ['U8', 'Str'],
};

for (const [name, expected] of Object.entries(ORDER)) {
  const body = new RegExp(`Function ${name}\\([^)]*\\)[\\s\\S]*?End Function`).exec(code);
  check(`the VB source has ${name}`, Boolean(body));
  if (!body) continue;
  const calls = [...body[0].matchAll(/\.(U8|U16|U32|I8|I16|I32|Str|Blob)\(/g)].map((m) => m[1]);
  check(`${name} writes its fields in the server's order`,
    JSON.stringify(calls) === JSON.stringify(expected),
    `\n     expected ${expected.join(' ')}\n     found    ${calls.join(' ')}`);
}

// ── The decoders the client needs, one per server message ───────────────────
for (const name of ['DecodeHelloAck', 'DecodeTitle', 'DecodeUrl', 'DecodeLoadState',
  'DecodeFramePayload', 'DecodeFindResult', 'DecodeAudio', 'DecodeError']) {
  check(`the VB source has ${name}`, new RegExp(`Function ${name}\\(`).test(code));
}

// Trailing bytes are an error rather than an ignored extension, or a layout
// difference between the two ends is silent.
const decoderCount = (code.match(/reader\.RequireEnd\(\)/g) || []).length;
check('every decoder rejects trailing bytes', decoderCount >= 8,
  `found ${decoderCount} RequireEnd call(s)`);

// ── The frame reader ────────────────────────────────────────────────────────
check('the frame reader buffers rather than assuming one frame per read',
  /Class RemoteFrameReader/.test(code));
// Asserted on the ORDER OF OPERATIONS, not on a sentence. An earlier version of
// this check looked for the words "compact first" and passed because they were in
// a comment -- which is a check that a comment exists, not a check that the code
// compacts. What matters is that the buffer is moved back to offset 0 before a
// larger one is allocated, or a two-megabyte frame doubles a nearly-full buffer.
const ensureRoom = /Private Sub EnsureRoom\([\s\S]*?End Sub/.exec(code);
check('the frame reader has an EnsureRoom', Boolean(ensureRoom));
check('the frame reader compacts before it grows', (() => {
  if (!ensureRoom) return false;
  const compactAt = ensureRoom[0].indexOf('_start = 0');
  const growAt = ensureRoom[0].indexOf('* 2 - 1');
  return compactAt >= 0 && growAt >= 0 && compactAt < growAt;
})());

console.log(`\n${checks - failures}/${checks} remote-protocol checks passed.`);
if (failures > 0) {
  console.log(`${failures} failure(s). The VB wire format and the server disagree, and the`);
  console.log('symptom on a phone would be a garbled screen rather than an error message.');
  process.exit(1);
}
console.log('The VB wire format reproduces the bytes the server builds.');
