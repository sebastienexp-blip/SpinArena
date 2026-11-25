// Photos de profil pour le jeu : Unity ne lit que le JPEG et le PNG, alors que TikTok fournit souvent du WebP.
// Le pont sert donc chaque photo sur http://127.0.0.1:<port>/photo?u=<lien> : JPEG/PNG transmis tels quels,
// WebP converti en PNG. Seuls les serveurs d'images de TikTok sont acceptés (jamais un lien arbitraire).

import fs from 'node:fs';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const ALLOWED = /(^|\.)(tiktokcdn\.com|tiktokcdn-us\.com|tiktokcdn-eu\.com|ibyteimg\.com|byteimg\.com|muscdn\.com)$/i;
const MAX_BYTES = 2_000_000;
const CACHE_MAX = 600;

export const photoStats = { servies: 0, converties: 0, echecs: 0 };
const cache = new Map(); // lien → { type, body }

let webp = null;
async function webpTools() {
  if (webp) return webp;
  const { PNG } = await import('pngjs');
  const mod = await import('@jsquash/webp/decode.js');
  const wasmPath = require.resolve('@jsquash/webp/codec/dec/webp_dec.wasm');
  await mod.init(await WebAssembly.compile(fs.readFileSync(wasmPath)));
  webp = { PNG, decode: mod.default };
  return webp;
}

export function isAllowedPhotoUrl(u) {
  if (typeof u !== 'string' || u.length > 1500) return false;
  try {
    const url = new URL(u);
    return url.protocol === 'https:' && ALLOWED.test(url.hostname);
  } catch { return false; }
}

/** Lien local à donner au jeu (vide si la photo n'est pas sur un serveur TikTok). */
export function localPhotoUrl(u, port) {
  return isAllowedPhotoUrl(u) ? `http://127.0.0.1:${port}/photo?u=${encodeURIComponent(u)}` : '';
}

export function sniff(buf) {
  if (buf.length > 12 && buf.toString('ascii', 0, 4) === 'RIFF' && buf.toString('ascii', 8, 12) === 'WEBP') return 'webp';
  if (buf.length > 3 && buf[0] === 0xff && buf[1] === 0xd8) return 'jpeg';
  if (buf.length > 8 && buf[0] === 0x89 && buf.toString('ascii', 1, 4) === 'PNG') return 'png';
  return 'autre';
}

export async function webpToPng(buf) {
  const { PNG, decode } = await webpTools();
  const img = await decode(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength));
  const png = new PNG({ width: img.width, height: img.height });
  png.data = Buffer.from(img.data.buffer, img.data.byteOffset, img.data.byteLength);
  return PNG.sync.write(png);
}

async function load(u) {
  const hit = cache.get(u);
  if (hit) return hit;
  const res = await fetch(u, { headers: { 'User-Agent': 'Mozilla/5.0', Accept: 'image/png,image/jpeg,image/webp,*/*' }, signal: AbortSignal.timeout(8000) });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  const buf = Buffer.from(await res.arrayBuffer());
  if (buf.length > MAX_BYTES) throw new Error('image trop lourde');
  const kind = sniff(buf);
  let out;
  if (kind === 'jpeg' || kind === 'png') out = { type: `image/${kind}`, body: buf };
  else if (kind === 'webp') { out = { type: 'image/png', body: await webpToPng(buf) }; photoStats.converties++; }
  else throw new Error('format inconnu');
  cache.set(u, out);
  if (cache.size > CACHE_MAX) cache.delete(cache.keys().next().value);
  return out;
}

/** Gestionnaire HTTP : renvoie true si la requête était pour une photo. */
export async function handlePhotoRequest(req, res) {
  const url = new URL(req.url, 'http://127.0.0.1');
  if (url.pathname !== '/photo') return false;
  const target = url.searchParams.get('u') || '';
  if (!isAllowedPhotoUrl(target)) { res.writeHead(403); res.end(); return true; }
  try {
    const img = await load(target);
    photoStats.servies++;
    res.writeHead(200, { 'Content-Type': img.type, 'Content-Length': img.body.length, 'Cache-Control': 'max-age=3600' });
    res.end(img.body);
  } catch (e) {
    photoStats.echecs++;
    res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end(String(e?.message || e));
  }
  return true;
}
