#!/usr/bin/env node
/**
 * BrowserForWP — X25519 field-arithmetic prototype and verifier.
 *
 * WHY THIS EXISTS
 * ---------------
 * BrowserForWP.Crypto/X25519.vb cannot be executed on macOS or Linux (the WP8.1
 * SDK is Windows-only), and `System.Numerics.BigInteger` is not confirmed to
 * exist in the ".NET for Windows Store apps" profile that a WP8.1 WinRT app
 * compiles against. So X25519 is implemented with fixed-width limbs that VB can
 * do natively with Int64 — and this script is where that arithmetic is PROVEN.
 *
 * It is a line-for-line prototype of X25519.vb: same limb layout, same fold
 * factors, same carry chain, same encode/decode. This script passing is what
 * makes the VB a transliteration instead of a fresh implementation.
 *
 * ── Radix choice ────────────────────────────────────────────────────────────
 * 16 limbs of 2^16. A power-of-two radix is not an arbitrary preference: it is
 * the only one where limb weights add EXACTLY, w(i+j) = w(i) + w(j), so a
 * product a[i]*b[j] lands in slot i+j with no correction factor.
 *
 * The radix-2^25.5 representation widely used elsewhere does NOT have that
 * property — there, w(1)+w(1) = 52 but w(2) = 51. Getting that right requires
 * reconstructing ref10's alternating-half-limb bookkeeping from memory, which is
 * exactly the kind of thing that produces wrong-but-plausible output. It was
 * tried first here and failed the multiply test.
 *
 * With 2^16 limbs there are exactly two fold factors, both derivable:
 *   p = 2^255 - 19  =>  2^255 = 19 (mod p)  and  2^256 = 38 (mod p)
 *   - products landing above limb 15 fold down one limb with 38
 *   - the top bit of limb 15 (weight 2^255) folds into limb 0 with 19
 *
 * Accumulators use BigInt purely so this script is exact, and every intermediate
 * is range-checked against the bound VB's Int64 must satisfy, so a value that
 * would overflow Int64 fails HERE rather than wrapping silently on the handset.
 *
 * Usage: node tools/proto/w25519.mjs
 */

const MASK16 = 0xffffn;
const P = (1n << 255n) - 19n;
const INT64_MAX = (1n << 63n) - 1n;
const LIMBS = 16;

/** p as 16 limbs of 2^16: FFED, FFFF x14, 7FFF. */
const P_LIMBS = (() => {
  let v = P;
  const h = new Array(LIMBS);
  for (let i = 0; i < LIMBS; i++) { h[i] = v & MASK16; v >>= 16n; }
  return h;
})();

/**
 * Offset added before a subtraction so no limb goes negative. Any multiple of p
 * is congruent to zero, so the value is unchanged; 4 is the smallest power of two
 * whose limbs all exceed the largest possible reduced limb (0xFFFF). The multiple
 * is computed, not transcribed.
 */
const SUB_OFFSET_MULTIPLE = (() => {
  let k = 1n;
  while (P_LIMBS.some((v) => k * v <= MASK16)) k *= 2n;
  return k;
})();
const SUB_OFFSET = P_LIMBS.map((v) => SUB_OFFSET_MULTIPLE * v);

let failures = 0;
let checks = 0;

function check(label, actual, expectedHex) {
  checks += 1;
  const want = expectedHex.replace(/\s+/g, '').toLowerCase();
  if (actual !== want) {
    failures += 1;
    console.error(`  x ${label}`);
    console.error(`      expected ${want}`);
    console.error(`      actual   ${actual}`);
  } else {
    console.log(`  + ${label}`);
  }
}

const h2b = (h) => Buffer.from(h.replace(/\s+/g, ''), 'hex');
const b2h = (b) => Buffer.from(b).toString('hex');

// ── Serialisation ──────────────────────────────────────────────────────────
function feFromBigInt(value) {
  let v = ((value % P) + P) % P;
  const h = new Array(LIMBS);
  for (let i = 0; i < LIMBS; i++) { h[i] = v & MASK16; v >>= 16n; }
  return h;
}

function feToBigInt(h) {
  let v = 0n;
  for (let i = LIMBS - 1; i >= 0; i--) { v <<= 16n; v += h[i]; }
  return v;
}

/** RFC 7748 §5 decode: 32 little-endian bytes, top bit of byte 31 masked. */
function feDecode(bytes) {
  const masked = Buffer.from(bytes);
  masked[31] &= 0x7f;
  const h = new Array(LIMBS);
  for (let i = 0; i < LIMBS; i++) {
    h[i] = BigInt(masked[2 * i]) | (BigInt(masked[2 * i + 1]) << 8n);
  }
  return h;
}

/** Canonical 32-byte little-endian encoding. */
function feEncode(input) {
  let t = feCarry(input.slice());

  const lessThan = (x, y) => {
    for (let i = LIMBS - 1; i >= 0; i--) {
      if (x[i] < y[i]) return true;
      if (x[i] > y[i]) return false;
    }
    return false;
  };
  const subP = (x) => {
    const out = new Array(LIMBS);
    let borrow = 0n;
    for (let i = 0; i < LIMBS; i++) {
      let v = x[i] - P_LIMBS[i] - borrow;
      if (v < 0n) { v += 1n << 16n; borrow = 1n; } else { borrow = 0n; }
      out[i] = v;
    }
    return out;
  };

  let guard = 0;
  while (!lessThan(t, P_LIMBS) && guard < 4) { t = subP(t); guard += 1; }

  const out = Buffer.alloc(32);
  for (let i = 0; i < LIMBS; i++) {
    out[2 * i] = Number(t[i] & 0xffn);
    out[2 * i + 1] = Number((t[i] >> 8n) & 0xffn);
  }
  return out;
}

// ── Field arithmetic ───────────────────────────────────────────────────────
function feCarry(t) {
  for (let pass = 0; pass < 3; pass++) {
    let stable = true;
    for (let i = 0; i < LIMBS - 1; i++) {
      const c = t[i] >> 16n;
      if (c !== 0n) { stable = false; }
      t[i] &= MASK16;
      t[i + 1] += c;
    }
    // Limb 15's bit 15 has weight 2^255, which is congruent to 19.
    const e = t[LIMBS - 1] >> 15n;
    if (e !== 0n) { stable = false; }
    t[LIMBS - 1] &= 0x7fffn;
    t[0] += 19n * e;
    if (stable) break;
  }

  for (let i = 0; i < LIMBS; i++) {
    if (t[i] < 0n || t[i] > MASK16) {
      throw new Error(`feCarry left limb ${i} out of range: ${t[i]}`);
    }
  }
  return t;
}

function feMul(a, b) {
  const t = new Array(2 * LIMBS - 1).fill(0n);
  for (let i = 0; i < LIMBS; i++) {
    if (a[i] === 0n) continue;
    for (let j = 0; j < LIMBS; j++) {
      t[i + j] += a[i] * b[j];
    }
  }

  // The exact value VB would hold in an Int64 at this point.
  let max = 0n;
  for (const v of t) if (v > max) max = v;
  if (max > INT64_MAX) {
    throw new Error(`feMul accumulator ${max} exceeds Int64 — the VB would overflow`);
  }

  // Limb k >= 16 has weight 2^(16k); folding it down one limb divides by 2^256,
  // which is congruent to 38 (mod p).
  for (let k = LIMBS; k < t.length; k++) {
    t[k - LIMBS] += 38n * t[k];
    t[k] = 0n;
  }

  const h = t.slice(0, LIMBS);
  let biggest = 0n;
  for (const v of h) if (v > biggest) biggest = v;
  if (biggest > INT64_MAX) {
    throw new Error(`feMul post-fold value ${biggest} exceeds Int64`);
  }

  return feCarry(h);
}

function feSq(a) {
  return feMul(a, a);
}

function feAdd(a, b) {
  const h = new Array(LIMBS);
  for (let i = 0; i < LIMBS; i++) h[i] = a[i] + b[i];
  return feCarry(h);
}

function feSub(a, b) {
  const h = new Array(LIMBS);
  for (let i = 0; i < LIMBS; i++) {
    h[i] = a[i] + SUB_OFFSET[i] - b[i];
    if (h[i] < 0n) throw new Error('feSub underflow: offset multiple too small');
  }
  return feCarry(h);
}

function feMulSmall(a, k) {
  const h = new Array(LIMBS);
  for (let i = 0; i < LIMBS; i++) h[i] = a[i] * BigInt(k);
  return feCarry(h);
}

/** Inversion by the standard addition chain for a^(p-2) = a^(2^255 - 21). */
function feInvert(a) {
  const z2 = feSq(a);
  // a^9 = a^8 * a, i.e. four squarings of z2 followed by one multiply by a.
  // Writing this as z2 * (a^2)^4 yields a^10 instead, which silently breaks
  // every inversion — and is exactly the bug this prototype caught.
  const z9 = feMul(feSq(feSq(z2)), a);
  const z11 = feMul(z9, z2);
  let t = feSq(z11);
  t = feMul(t, z9);
  const z2_5_0 = t;                                   // a^(2^5 - 1)

  t = z2_5_0;
  for (let i = 0; i < 5; i++) t = feSq(t);
  const z2_10_0 = feMul(t, z2_5_0);                   // a^(2^10 - 1)

  t = z2_10_0;
  for (let i = 0; i < 10; i++) t = feSq(t);
  const z2_20_0 = feMul(t, z2_10_0);                  // a^(2^20 - 1)

  t = z2_20_0;
  for (let i = 0; i < 20; i++) t = feSq(t);
  const z2_40_0 = feMul(t, z2_20_0);                  // a^(2^40 - 1)

  t = z2_40_0;
  for (let i = 0; i < 10; i++) t = feSq(t);
  const z2_50_0 = feMul(t, z2_10_0);                  // a^(2^50 - 1)

  t = z2_50_0;
  for (let i = 0; i < 50; i++) t = feSq(t);
  const z2_100_0 = feMul(t, z2_50_0);                 // a^(2^100 - 1)

  t = z2_100_0;
  for (let i = 0; i < 100; i++) t = feSq(t);
  const z2_200_0 = feMul(t, z2_100_0);                // a^(2^200 - 1)

  t = z2_200_0;
  for (let i = 0; i < 50; i++) t = feSq(t);
  const z2_250_0 = feMul(t, z2_50_0);                 // a^(2^250 - 1)

  t = z2_250_0;
  for (let i = 0; i < 5; i++) t = feSq(t);
  return feMul(t, z11);                               // a^(2^255 - 21)
}

// ── Clamping and the Montgomery ladder ─────────────────────────────────────
function clamp(k) {
  const out = Buffer.from(k);
  out[0] &= 0xf8;
  out[31] &= 0x7f;
  out[31] |= 0x40;
  return out;
}

function x25519(scalar, uCoordinate) {
  const k = clamp(scalar);
  const x1 = feDecode(uCoordinate);

  let x2 = feFromBigInt(1n);
  let z2 = new Array(LIMBS).fill(0n);
  let x3 = x1.slice();
  let z3 = feFromBigInt(1n);
  let swap = 0;

  for (let t = 254; t >= 0; t--) {
    const kt = (k[t >> 3] >> (t & 7)) & 1;
    swap ^= kt;
    if (swap) { [x2, x3] = [x3, x2]; [z2, z3] = [z3, z2]; }
    swap = kt;

    const a = feAdd(x2, z2);
    const aa = feSq(a);
    const b = feSub(x2, z2);
    const bb = feSq(b);
    const e = feSub(aa, bb);
    const c = feAdd(x3, z3);
    const d = feSub(x3, z3);
    const da = feMul(d, a);
    const cb = feMul(c, b);
    const daPlus = feAdd(da, cb);
    const daMinus = feSub(da, cb);
    x3 = feSq(daPlus);
    z3 = feMul(x1, feSq(daMinus));
    x2 = feMul(aa, bb);
    // RFC 7748 §5: z_2 = E * (AA + a24 * E), with a24 = 121665.
    // Using BB here instead of AA is a silent one-character-class error that
    // still produces plausible-looking output.
    z2 = feMul(e, feAdd(aa, feMulSmall(e, 121665)));
  }

  if (swap) { [x2, x3] = [x3, x2]; [z2, z3] = [z3, z2]; }

  return feEncode(feMul(x2, feInvert(z2)));
}

function x25519Public(privateKey) {
  const base = Buffer.alloc(32);
  base[0] = 9;
  return x25519(privateKey, base);
}

// ══════════════════════════════════════════════════════════════════════════
console.log('\n[0/4] Field operations vs direct BigInt arithmetic');
console.log('  p limbs (2^16):', P_LIMBS.map((v) => '&H' + v.toString(16).toUpperCase()).join(', '));
console.log('  subtraction offset multiple:', SUB_OFFSET_MULTIPLE.toString());

(() => {
  const mod = (v) => ((v % P) + P) % P;
  let seed = 0x2545f4914f6cdd1dn;
  const nextRand = () => {
    seed ^= seed << 13n; seed &= (1n << 64n) - 1n;
    seed ^= seed >> 7n;
    seed ^= seed << 17n; seed &= (1n << 64n) - 1n;
    return seed;
  };

  const cases = [];
  for (let i = 0; i < 60; i++) {
    cases.push([mod(nextRand() + (nextRand() << 64n) + (nextRand() << 128n) + (nextRand() << 192n)),
                mod(nextRand() + (nextRand() << 64n) + (nextRand() << 128n) + (nextRand() << 192n))]);
  }
  cases.push([0n, P - 1n], [P - 1n, 0n], [0n, 0n], [1n, 1n], [P - 1n, P - 1n], [1n, P - 1n]);

  // Per-operation counters, so a failure names the operation instead of only
  // showing that the final vector is wrong.
  const opFail = { add: 0, sub: 0, mul: 0, sq: 0, inv: 0 };
  for (const [x, y] of cases) {
    const fx = feFromBigInt(x);
    const fy = feFromBigInt(y);
    // Compare CONGRUENCE, not equality. A field element carries a redundant
    // representation: the result of an addition may be the limb encoding of
    // 1 + k*p rather than of 1, which is the same field element. Only feEncode
    // produces a canonical value, so the comparison must reduce first.
    const eq = (got, want) => (feToBigInt(got) % P) === mod(want);
    if (!eq(feAdd(fx, fy), x + y)) opFail.add += 1;
    if (!eq(feSub(fx, fy), x - y)) opFail.sub += 1;
    if (!eq(feMul(fx, fy), x * y)) opFail.mul += 1;
    if (!eq(feSq(fx), x * x)) opFail.sq += 1;
    if (x !== 0n && !eq(feMul(fx, feInvert(fx)), 1n)) opFail.inv += 1;
  }
  const total = Object.values(opFail).reduce((a, b) => a + b, 0);
  checks += 1;
  if (total === 0) {
    console.log(`  + ${cases.length} randomised cases: add/sub/mul/sq/invert all match BigInt`);
  } else {
    failures += 1;
    console.error(`  x ${total} mismatches across ${cases.length} cases: ` +
      Object.entries(opFail).filter(([, n]) => n > 0).map(([k, n]) => `${k}=${n}`).join(' '));
    // Show one concrete failing input per broken operation.
    for (const [x, y] of cases) {
      const fx = feFromBigInt(x); const fy = feFromBigInt(y);
      if (opFail.add && (feToBigInt(feAdd(fx, fy)) % P) !== mod(x + y)) { console.error(`      add fail: ${x} + ${y}`); opFail.add = 0; }
      if (opFail.sub && (feToBigInt(feSub(fx, fy)) % P) !== mod(x - y)) { console.error(`      sub fail: ${x} - ${y}`); opFail.sub = 0; }
      if (opFail.mul && (feToBigInt(feMul(fx, fy)) % P) !== mod(x * y)) { console.error(`      mul fail: ${x} * ${y}`); opFail.mul = 0; }
      if (opFail.sq && (feToBigInt(feSq(fx)) % P) !== mod(x * x)) { console.error(`      sq fail: ${x}`); opFail.sq = 0; }
      if (opFail.inv && x !== 0n && (feToBigInt(feMul(fx, feInvert(fx))) % P) !== 1n) { console.error(`      inv fail: ${x}`); opFail.inv = 0; }
    }
  }

  let encBad = 0;
  for (const [x] of cases) {
    // Encode produces little-endian bytes; compare against the reversed hex.
    const want = Buffer.from(x.toString(16).padStart(64, '0'), 'hex').reverse().toString('hex');
    if (b2h(feEncode(feFromBigInt(x))) !== want) encBad += 1;
  }
  checks += 1;
  if (encBad === 0) console.log('  + encode(x) is canonical little-endian for every case');
  else { failures += 1; console.error(`  x ${encBad} encode failures`); }

  let decBad = 0;
  for (const [x] of cases) {
    if (feToBigInt(feDecode(feEncode(feFromBigInt(x)))) !== x) decBad += 1;
  }
  checks += 1;
  if (decBad === 0) console.log('  + decode(encode(x)) round-trips for every case');
  else { failures += 1; console.error(`  x ${decBad} decode failures`); }
})();

console.log('\n[1/4] Field element sanity');
check('encode(1)', b2h(feEncode(feFromBigInt(1n))), '01' + '00'.repeat(31));
check('encode(p-1) = ec ff..ff 7f',
  b2h(feEncode(feFromBigInt(P - 1n))), 'ec' + 'ff'.repeat(30) + '7f');
check('encode(p) = 0 (canonical reduction)', b2h(feEncode(feFromBigInt(P))), '00'.repeat(32));
check('encode(p+1) = 1', b2h(feEncode(feFromBigInt(P + 1n))), '01' + '00'.repeat(31));
check('invert(1) = 1', b2h(feEncode(feInvert(feFromBigInt(1n)))), '01' + '00'.repeat(31));
check('5 * invert(5) = 1',
  b2h(feEncode(feMul(feFromBigInt(5n), feInvert(feFromBigInt(5n))))), '01' + '00'.repeat(31));

console.log('\n[2/4] RFC 7748 §5.2 — raw scalar multiplication');
check('scalar mult', b2h(x25519(
  h2b('a546e36bf0527c9d3b16154b82465edd62144c0ac1fc5a18506a2244ba449ac4'),
  h2b('e6db6867583030db3594c1a424b15f7c726624ec26b3353b10a903a6d0ab1c4c'))),
  'c3da55379de9c6908e94ea4df28d084f32eccf03491c71f754b4075577a28552');

check('scalar mult, high bit of u set', b2h(x25519(
  h2b('4b66e9d4d1b4673c5ad22691957d6af5c11b6421e0ea01d42ca4169e7918ba0d'),
  h2b('e5210f12786811d3f4b7959d0538ae2c31dbe7106fc03c3efc4cd549c715a493'))),
  '95cbde9476e8907d7aade45cb4b873f88b595a68799fa152e6f8f7647aac7957');

console.log('\n[3/4] RFC 7748 §6.1 — Diffie-Hellman');
const alicePriv = h2b('77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a');
const alicePub = '8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a';
const bobPriv = h2b('5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb');
const bobPub = 'de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f';
const shared = '4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742';

check('Alice public key', b2h(x25519Public(alicePriv)), alicePub);
check('Bob public key', b2h(x25519Public(bobPriv)), bobPub);
check('shared secret (Alice side)', b2h(x25519(alicePriv, h2b(bobPub))), shared);
check('shared secret (Bob side)', b2h(x25519(bobPriv, h2b(alicePub))), shared);

console.log('\n[4/4] RFC 8448 §3 — the handshake this project actually performs');
check('client ephemeral public key',
  b2h(x25519Public(h2b('49af42ba7f7994852d713ef2784bcbcaa7911de26adc5642cb634540e7ea5005'))),
  '99381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c');
check('server ephemeral public key',
  b2h(x25519Public(h2b('b1580eeadf6dd589b8ef4f2d5652578cc810e9980191ec8d058308cea216a21e'))),
  'c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f');
check('ECDHE shared secret',
  b2h(x25519(h2b('49af42ba7f7994852d713ef2784bcbcaa7911de26adc5642cb634540e7ea5005'),
             h2b('c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f'))),
  '8bd4054fb55b9d63fdfbacf9f04b9f0d35e6d63f537563efd46272900f89492d');

console.log(`\n${checks} checks, ${failures} failure(s)`);
if (failures > 0) {
  console.error('\nField arithmetic does NOT reproduce the RFC vectors. Do not transliterate to VB.');
  process.exit(1);
}
console.log('All RFC 7748 / 8448 vectors reproduced.');
console.log('BrowserForWP.Crypto/X25519.vb is a transliteration of this file.');
