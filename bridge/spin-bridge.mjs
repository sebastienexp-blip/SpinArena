#!/usr/bin/env node
// SPIN ARENA — pont TikTok LIVE pour Termux.
//
// Lit le chat, les cadeaux, les likes et les abonnements de TON live TikTok avec la bibliothèque
// non officielle TikTok-Live-Connector (TikTok ne propose pas d'API publique pour cela), puis les
// transmet au jeu par WebSocket local (ws://127.0.0.1:8787). Le jeu et le pont tournent sur le même téléphone.
//
// Utilisation : node spin-bridge.mjs <ton_pseudo_tiktok>        (ou réglé une fois dans ~/.spin-bridge.json)
//               node spin-bridge.mjs --test                      (événements de test, sans live, pour vérifier le lien avec le jeu)

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { WebSocketServer } from 'ws';
import { handlePhotoRequest, localPhotoUrl, photoStats as photoServed } from './photos.mjs';
import { normalizeChat, normalizeGift, normalizeLike, normalizeSimple, dedupKey } from './normalize.mjs';

const VERSION = '0.2.0';
const CONFIG_PATH = path.join(os.homedir(), '.spin-bridge.json');
const args = process.argv.slice(2);
const testMode = args.includes('--test');

let config = { username: '', port: 8787, signApiKey: '' };
try { config = { ...config, ...JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8')) }; } catch { /* pas encore de configuration */ }
const cliUser = args.find((a) => !a.startsWith('--'));
if (cliUser) {
  config.username = cliUser.replace(/^@/, '');
  try { fs.writeFileSync(CONFIG_PATH, JSON.stringify(config, null, 2)); } catch { /* lecture seule */ }
}

const log = (...m) => console.log(new Date().toLocaleTimeString('fr-FR'), ...m);

// ------------------------------------------------------------------ serveur local pour le jeu

// Un seul port : WebSocket pour les événements, HTTP /photo pour les photos de profil converties.
const server = http.createServer(async (req, res) => {
  if (await handlePhotoRequest(req, res)) return;
  res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
  res.end(`SPIN ARENA pont v${VERSION}`);
});
const wss = new WebSocketServer({ server });
const photoStats = {};
const stats = { chat: 0, gift: 0, like: 0, member: 0, follow: 0, duplicates: 0, games: 0, errors: 0 };
let tiktokStatus = { status: 'starting', detail: '' };

function broadcast(ev) {
  const json = JSON.stringify(ev);
  for (const c of wss.clients) if (c.readyState === 1) c.send(json);
}

function sendStatus(status, detail = '') {
  tiktokStatus = { status, detail };
  broadcast({ type: 'status', status, detail });
}

wss.on('connection', (ws) => {
  stats.games++;
  log('🎮 Jeu connecté au pont');
  ws.send(JSON.stringify({ type: 'status', status: tiktokStatus.status, detail: tiktokStatus.detail }));
  ws.on('close', () => log('🎮 Jeu déconnecté'));
});
server.listen(config.port, '127.0.0.1');
server.on('error', (e) => {
  console.error(`Impossible d'ouvrir le port ${config.port} : ${e.message}. Le pont tourne peut-être déjà dans une autre session Termux.`);
  process.exit(1);
});

// ------------------------------------------------------------------ anti-doublons

const seen = new Set();
const seenOrder = [];
function emit(ev) {
  if (!ev.user || !(ev.user.userId || ev.user.uniqueId)) return;
  const key = dedupKey(ev);
  if (key) {
    if (seen.has(key)) { stats.duplicates++; return; }
    seen.add(key); seenOrder.push(key);
    if (seenOrder.length > 5000) seen.delete(seenOrder.shift());
  }
  stats[ev.type] = (stats[ev.type] || 0) + 1;
  // Diagnostic des photos de profil : quel format TikTok fournit-il ?
  const url = ev.user.avatarUrl || '';
  const fmt = !url ? 'aucune' : /\.(jpe?g|png)(\?|$)/i.test(url) ? 'jpeg' : /\.webp(\?|$)/i.test(url) ? 'webp' : 'autre';
  photoStats[fmt] = (photoStats[fmt] || 0) + 1;
  // Le jeu reçoit un lien local : le pont lui sert la photo en JPEG/PNG (WebP converti).
  if (url) ev.user.avatarUrl = localPhotoUrl(url, config.port) || url;
  broadcast(ev);
}

// ------------------------------------------------------------------ mode test (sans live)

if (testMode) {
  log(`🧪 Mode test : événements simulés (marqués TEST), port ${config.port}`);
  sendStatus('test', 'événements simulés');
  let i = 0;
  setInterval(() => {
    i++;
    const user = { userId: `test_${i % 6}`, uniqueId: `test_${i % 6}`, nickname: `TEST ${i % 6}`, avatarUrl: '' };
    emit({ type: 'chat', msgId: `t${i}`, user, comment: i % 5 === 0 ? '!attaque' : '!join' });
    if (i % 7 === 0) emit({ type: 'gift', msgId: `g${i}`, user, giftId: '5655', giftName: 'Rose', diamondCount: 1, repeatCount: 1, repeatEnd: true, streakable: false, groupId: '', giftImageUrl: '' });
  }, 1500);
} else {
  startTikTok();
}

// ------------------------------------------------------------------ connexion TikTok

async function startTikTok() {
  if (!config.username) {
    console.error('Indique ton pseudo TikTok : node spin-bridge.mjs ton_pseudo   (il sera mémorisé)');
    process.exit(1);
  }
  let lib;
  try { lib = await import('tiktok-live-connector'); }
  catch {
    console.error('Bibliothèque absente : lance « npm install » dans le dossier du pont.');
    process.exit(1);
  }
  const m = lib.default ?? lib;
  const Conn = m.TikTokLiveConnection ?? lib.TikTokLiveConnection ?? m.WebcastPushConnection ?? lib.WebcastPushConnection;
  const E = m.WebcastEvent ?? lib.WebcastEvent ?? {};
  const C = m.ControlEvent ?? lib.ControlEvent ?? {};
  const ev = (name, legacy) => E[name] ?? legacy;
  // enableExtendedGiftInfo reste désactivé : la liste des cadeaux passe par un service de signature payant
  // (offre « Business » d'Euler Stream). Les événements cadeaux contiennent déjà le nom, la valeur et l'image.
  const options = { processInitialData: false, enableExtendedGiftInfo: false, fetchRoomInfoOnConnect: true };
  if (config.signApiKey) options.signApiKey = config.signApiKey;

  log(`SPIN ARENA pont v${VERSION} — live @${config.username} — jeu sur ws://127.0.0.1:${config.port}`);
  let delay = 3000;
  let conn = null;
  let connecting = false;

  const connect = async () => {
    if (connecting) return;
    connecting = true;
    try { conn?.removeAllListeners?.(); conn?.disconnect?.(); } catch { /* ignore */ }
    conn = new Conn(config.username, options);
    conn.on(ev('CHAT', 'chat'), (d) => emit(normalizeChat(d)));
    conn.on(ev('GIFT', 'gift'), (d) => emit(normalizeGift(d)));
    conn.on(ev('LIKE', 'like'), (d) => emit(normalizeLike(d)));
    conn.on(ev('FOLLOW', 'follow'), (d) => emit(normalizeSimple('follow', d)));
    conn.on(ev('SHARE', 'share'), (d) => emit(normalizeSimple('share', d)));
    conn.on(ev('MEMBER', 'member'), (d) => emit(normalizeSimple('member', d)));
    conn.on(ev('STREAM_END', 'streamEnd'), () => { sendStatus('ended', 'live terminé'); log('⏹ Live terminé'); retry(30000); });
    conn.on(C.DISCONNECTED ?? 'disconnected', () => { sendStatus('disconnected'); log('⚠️ Déconnecté de TikTok'); retry(); });
    // Les petits incidents de décodage ne coupent pas la connexion (leçon du quiz) : on les compte seulement.
    conn.on(C.ERROR ?? 'error', (e) => { stats.errors++; if (stats.errors % 20 === 1) log('incident TikTok :', e?.info || e?.message || e); });
    try {
      const state = await conn.connect();
      delay = 3000;
      sendStatus('connected', `@${config.username}`);
      log(`✅ Connecté au live (salle ${state?.roomId ?? '?'})`);
    } catch (e) {
      const msg = String(e?.message || e);
      const offline = /offline|not.?live|isn'?t online|LIVE has ended|user_not_found|19881007|room ?id/i.test(msg);
      sendStatus(offline ? 'offline' : 'error', offline ? 'pas de live en cours' : msg.slice(0, 120));
      log(offline ? '⏳ Pas de live en cours, nouvel essai bientôt…' : `❌ Connexion impossible : ${msg}`);
      retry(offline ? 20000 : undefined);
    } finally {
      connecting = false;
    }
  };

  let timer = null;
  const retry = (ms) => {
    if (timer) return;
    const wait = ms ?? delay;
    delay = Math.min(delay * 2, 60000);
    timer = setTimeout(() => { timer = null; connect(); }, wait);
  };

  connect();
}

setInterval(() => {
  log(`📊 chat ${stats.chat} · dons ${stats.gift} · likes ${stats.like} · abonnés ${stats.follow} · doublons ${stats.duplicates} · jeux connectés ${wss.clients.size} · TikTok : ${tiktokStatus.status} · photos ${JSON.stringify(photoStats)} · servies ${JSON.stringify(photoServed)}`);
}, 60000);

process.on('SIGINT', () => { log('Arrêt du pont'); process.exit(0); });
