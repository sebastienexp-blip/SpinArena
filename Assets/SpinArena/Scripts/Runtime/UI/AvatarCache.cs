using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SpinArena.Core.Tops;
using UnityEngine;
using UnityEngine.Networking;

namespace SpinArena.UI
{
    /// <summary>
    /// Photos de profil : téléchargement (4 à la fois au plus), découpe ronde, cache mémoire et disque.
    /// Si la photo n'est pas disponible, un avatar est généré à partir de l'identifiant (motif symétrique coloré) :
    /// rien n'est inventé sur la personne.
    /// </summary>
    public sealed class AvatarCache : MonoBehaviour
    {
        public static AvatarCache Instance { get; private set; }

        const int Size = 128;
        const int MaxConcurrent = 4;
        const int MaxMemory = 220;

        readonly Dictionary<string, Texture2D> _photos = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Texture2D> _generated = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, List<Action<Texture2D>>> _waiting = new Dictionary<string, List<Action<Texture2D>>>();
        readonly Queue<string> _queue = new Queue<string>();
        readonly LinkedList<string> _lru = new LinkedList<string>();
        int _active;
        string _diskDir;

        public int Downloaded { get; private set; }
        public int Failed { get; private set; }

        void Awake()
        {
            Instance = this;
            _diskDir = Path.Combine(Application.persistentDataPath, "avatars");
            try { Directory.CreateDirectory(_diskDir); } catch (Exception) { _diskDir = null; }
        }

        /// <summary>Avatar immédiat (photo en cache ou avatar généré), puis callback quand la vraie photo arrive.</summary>
        public Texture2D Get(string playerId, string url, Action<Texture2D> onPhoto)
        {
            if (!string.IsNullOrEmpty(url))
            {
                string key = Key(url, playerId);
                if (_photos.TryGetValue(key, out var t) && t != null) { Touch(key); return t; }
                if (TryDisk(key, out t)) { Store(key, t); return t; }
                if (!_waiting.TryGetValue(key, out var list))
                {
                    _waiting[key] = list = new List<Action<Texture2D>>();
                    _queue.Enqueue(url + "\n" + key);
                }
                if (onPhoto != null) list.Add(onPhoto);
            }
            return Generated(playerId);
        }

        static string Key(string url, string playerId)
        {
            // Les adresses TikTok sont signées et changent : on indexe par joueur + chemin de l'image sans paramètres.
            int q = url.IndexOf('?');
            string path = q > 0 ? url.Substring(0, q) : url;
            return playerId + "_" + TopCatalog.StableHash(path).ToString("x8");
        }

        void Update()
        {
            while (_active < MaxConcurrent && _queue.Count > 0)
            {
                var parts = _queue.Dequeue().Split('\n');
                StartCoroutine(Download(parts[0], parts[1]));
            }
        }

        public string LastError { get; private set; }

        IEnumerator Download(string url, string key)
        {
            _active++;
            Texture2D result = null;
            // Unity ne lit que le JPEG et le PNG : si TikTok n'a fourni que du WebP, on demande la même image en JPEG.
            var attempts = new List<string> { url };
            // Le pont (v0.2+) sert déjà la photo convertie en PNG sur 127.0.0.1 : rien à deviner dans ce cas.
            if (url.Contains(".webp") && !url.StartsWith("http://127.0.0.1")) attempts.Add(url.Replace(".webp", ".jpeg"));
            foreach (var u in attempts)
            {
                using (var req = UnityWebRequestTexture.GetTexture(u, true))
                {
                    req.timeout = 10;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { LastError = req.error; continue; }
                    Texture2D raw = null;
                    try { raw = DownloadHandlerTexture.GetContent(req); }
                    catch (Exception e) { LastError = e.Message; }
                    // Image illisible : Unity renvoie une petite texture « ? » de 8×8.
                    if (raw == null || raw.width <= 8) { LastError = "format d'image non lu (" + System.IO.Path.GetExtension(u.Split('?')[0]) + ")"; if (raw != null) Destroy(raw); continue; }
                    result = MakeRound(raw);
                    Destroy(raw);
                    break;
                }
            }
            {
                if (result != null)
                {
                    Store(key, result);
                    SaveDisk(key, result);
                    Downloaded++;
                }
                else
                {
                    Failed++;
                    Debug.LogWarning("[SpinArena] Photo de profil non chargée : " + LastError);
                }
                if (_waiting.TryGetValue(key, out var list))
                {
                    _waiting.Remove(key);
                    if (result != null) foreach (var cb in list) { try { cb(result); } catch (Exception e) { Debug.LogException(e); } }
                }
            }
            _active--;
        }

        void Store(string key, Texture2D t)
        {
            _photos[key] = t;
            Touch(key);
            while (_lru.Count > MaxMemory)
            {
                string old = _lru.Last.Value;
                _lru.RemoveLast();
                // On ne détruit pas la texture : elle peut encore être affichée ; le ramasse-miettes s'en charge.
                _photos.Remove(old);
            }
        }

        void Touch(string key)
        {
            _lru.Remove(key);
            _lru.AddFirst(key);
        }

        bool TryDisk(string key, out Texture2D t)
        {
            t = null;
            if (_diskDir == null) return false;
            string p = Path.Combine(_diskDir, key + ".png");
            if (!File.Exists(p)) return false;
            try
            {
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(p)).TotalDays > 7) return false; // photo rafraîchie chaque semaine
                t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(File.ReadAllBytes(p))) { Destroy(t); t = null; return false; }
                t.wrapMode = TextureWrapMode.Clamp;
                return true;
            }
            catch (Exception) { return false; }
        }

        void SaveDisk(string key, Texture2D t)
        {
            if (_diskDir == null) return;
            try { File.WriteAllBytes(Path.Combine(_diskDir, key + ".png"), t.EncodeToPNG()); }
            catch (Exception e) { Debug.LogWarning("[SpinArena] Cache avatar : " + e.Message); }
        }

        /// <summary>Redimensionne et découpe la photo en disque avec un liseré.</summary>
        static Texture2D MakeRound(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Avatar" };
            t.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            ApplyCircle(px, new Color32(255, 255, 255, 255));
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        static void ApplyCircle(Color32[] px, Color32 border)
        {
            float c = (Size - 1) * 0.5f, r = Size * 0.5f - 1f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                int i = y * Size + x;
                if (d > r) { px[i].a = 0; continue; }
                if (d > r - 5f) { float k = Mathf.Clamp01((d - (r - 5f)) / 1.5f); px[i] = Color32.Lerp(px[i], border, k); }
                if (d > r - 1f) px[i].a = (byte)(Mathf.Clamp01(r - d + 1f) * 255f);
            }
        }

        /// <summary>Avatar de remplacement : motif 5×5 symétrique et couleur dérivés de l'identifiant.</summary>
        public Texture2D Generated(string playerId)
        {
            playerId = playerId ?? "?";
            if (_generated.TryGetValue(playerId, out var t)) return t;
            uint h = TopCatalog.StableHash(playerId);
            Color fg = Color.HSVToRGB((h % 360) / 360f, 0.65f, 0.95f);
            Color bg = Color.HSVToRGB(((h / 360) % 360) / 360f, 0.5f, 0.22f);
            var px = new Color32[Size * Size];
            int cell = Size / 7;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int cx = x / cell - 1, cy = y / cell - 1;
                bool on = false;
                if (cx >= 0 && cx < 5 && cy >= 0 && cy < 5)
                {
                    int mx = cx < 3 ? cx : 4 - cx;
                    on = ((h >> (mx * 5 + cy)) & 1) != 0;
                }
                px[y * Size + x] = on ? fg : bg;
            }
            ApplyCircle(px, new Color32(255, 255, 255, 255));
            t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "GenAvatar" };
            t.SetPixels32(px);
            t.Apply(false, false);
            _generated[playerId] = t;
            return t;
        }
    }
}
