// Conversion des événements de TikTok-Live-Connector (format v2 « protobuf » ou ancien format aplati)
// vers le format simple attendu par SPIN ARENA. Aucune donnée n'est inventée : un champ absent reste vide.

const str = (v) => (v === undefined || v === null ? '' : String(v));

function firstUrl(img) {
  if (!img) return '';
  if (typeof img === 'string') return img;
  const list = img.urlList || img.url_list || img.url || [];
  if (Array.isArray(list)) return bestImageUrl(list);
  return '';
}

/**
 * Choisit l'image que le jeu sait afficher : Unity ne lit que le JPEG et le PNG
 * (pas le WebP ni le HEIC, pourtant souvent proposés en premier par TikTok).
 */
export function bestImageUrl(list) {
  const urls = (list || []).filter((u) => typeof u === 'string' && u.startsWith('http'));
  const isJpgPng = (u) => /\.(jpe?g|png)(\?|$)/i.test(u) || /[?&]format=(jpe?g|png)/i.test(u);
  return urls.find(isJpgPng) || urls.find((u) => !/\.(heic|webp)(\?|$)/i.test(u)) || urls.find((u) => !/\.heic(\?|$)/i.test(u)) || urls[0] || '';
}

export function normalizeUser(d) {
  const u = (d && (d.user || d.userDetails)) || d || {};
  const userId = str(u.userId || u.id || d?.userId);
  const uniqueId = str(u.uniqueId || u.displayId || d?.uniqueId);
  const nickname = str(u.nickname || d?.nickname);
  // Toutes les tailles proposées sont regroupées pour trouver une image en JPEG/PNG si TikTok en fournit une.
  const lists = [u.avatarThumb, u.avatarMedium, u.profilePicture, u.avatarLarge]
    .flatMap((img) => (img && (img.urlList || img.url_list || img.urls || img.url)) || [])
    .filter((x) => typeof x === 'string')
    .concat([u.profilePictureUrl, d?.profilePictureUrl].filter(Boolean));
  const avatarUrl = bestImageUrl(lists);
  return { userId, uniqueId, nickname, avatarUrl };
}

function msgIdOf(d) {
  return str(d?.common?.msgId || d?.msgId || d?.event?.msgId || d?.common?.msg_id);
}

export function normalizeChat(d) {
  return { type: 'chat', msgId: msgIdOf(d), user: normalizeUser(d), comment: str(d?.comment ?? d?.content) };
}

export function normalizeGift(d) {
  const gift = d?.gift || {};
  const details = d?.giftDetails || {};
  const giftType = Number(gift.type ?? details.giftType ?? d?.giftType ?? 0);
  const repeatEnd = d?.repeatEnd === true || Number(d?.repeatEnd) === 1;
  return {
    type: 'gift',
    msgId: msgIdOf(d),
    user: normalizeUser(d),
    giftId: str(d?.giftId || gift.id),
    giftName: str(gift.name || details.giftName || d?.giftName || d?.extendedGiftInfo?.name),
    diamondCount: Number(gift.diamondCount ?? details.diamondCount ?? d?.diamondCount ?? 0) || 0,
    repeatCount: Math.max(1, Number(d?.repeatCount ?? d?.comboCount ?? 1) || 1),
    repeatEnd,
    // giftType 1 = cadeau enchaînable (série) ; les autres arrivent en un seul message.
    streakable: giftType === 1,
    groupId: str(d?.groupId),
    giftImageUrl: firstUrl(gift.image) || firstUrl(gift.icon) || str(details?.giftImage?.giftPictureUrl || d?.giftPictureUrl),
  };
}

export function normalizeLike(d) {
  return { type: 'like', msgId: msgIdOf(d), user: normalizeUser(d), likeCount: Math.max(1, Number(d?.count ?? d?.likeCount ?? 1) || 1) };
}

export function normalizeSimple(type, d) {
  return { type, msgId: msgIdOf(d), user: normalizeUser(d) };
}

/** Clé anti-doublon (les reconnexions peuvent renvoyer des messages déjà reçus). */
export function dedupKey(ev) {
  if (!ev.msgId) return '';
  return ev.type === 'gift' ? `${ev.msgId}|${ev.repeatCount}|${ev.repeatEnd ? 1 : 0}` : `${ev.type}|${ev.msgId}`;
}
