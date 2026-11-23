using System.Collections.Generic;
using SpinArena.Core.Battle;
using SpinArena.Core.Players;
using SpinArena.Core.Tops;
using SpinArena.Tops;
using SpinArena.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpinArena.UI
{
    /// <summary>
    /// Interface du live (1920×1080, lisible sur téléphone) : titre, phase, chrono, survivants, liste des joueurs
    /// avec photo et énergie, fil d'événements, annonces, carte d'entrée, classement et écran du vainqueur.
    /// </summary>
    public sealed class HudManager : MonoBehaviour
    {
        public static HudManager Instance { get; private set; }
        public Canvas Canvas { get; private set; }

        Text _phase, _timer, _survivors, _status, _hint, _demoTag, _banner, _bannerSub;
        RectTransform _listRoot, _joinCard, _winnerCard, _boardCard;
        Text _joinName, _joinTop, _winnerName, _winnerSub, _boardText;
        RawImage _joinAvatar, _winnerAvatar;
        CanvasGroup _bannerGroup, _joinGroup, _winnerGroup, _boardGroup;
        float _bannerUntil, _joinUntil, _boardUntil;
        bool _winnerVisible;
        readonly Queue<(string name, Texture tex, string top, Color rc)> _joinQueue = new Queue<(string, Texture, string, Color)>();

        sealed class Row { public RectTransform root; public RawImage avatar; public Text name; public Image bar; public Text level; }
        readonly List<Row> _rows = new List<Row>();
        Image _hintPanel;
        RectTransform _giftCard;
        CanvasGroup _giftGroup;
        RawImage _giftIcon;
        Text _giftName, _giftEffect;
        float _giftUntil;
        readonly Queue<(string user, Texture icon, string effect)> _giftQueue = new Queue<(string, Texture, string)>();
        const int Rows = 10;

        void Awake()
        {
            Instance = this;
            var cgo = new GameObject("HUD", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            Canvas = cgo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            Build(Canvas.transform);
        }

        void Build(Transform root)
        {
            Vector2 top = new Vector2(0.5f, 1f), tl = new Vector2(0f, 1f), tr = new Vector2(1f, 1f), bottom = new Vector2(0.5f, 0f), center = new Vector2(0.5f, 0.5f);

            // Bandeau du haut.
            var bar = UiFactory.Panel("TopBar", root, top, top, new Vector2(0, -14), new Vector2(760, 112), UiFactory.PanelColor);
            UiFactory.Label("Title", bar.transform, "<color=#5CE1FF>SPIN</color> ARENA", 50, TextAnchor.MiddleCenter, Color.white, top, top, new Vector2(0, -6), new Vector2(740, 60));
            _phase = UiFactory.Label("Phase", bar.transform, "", 28, TextAnchor.MiddleLeft, UiFactory.Accent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(26, 8), new Vector2(320, 40));
            _timer = UiFactory.Label("Timer", bar.transform, "", 30, TextAnchor.MiddleCenter, Color.white, bottom, bottom, new Vector2(0, 8), new Vector2(160, 40));
            _survivors = UiFactory.Label("Survivors", bar.transform, "", 28, TextAnchor.MiddleRight, UiFactory.Gold, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-26, 8), new Vector2(320, 40));

            // Liste des joueurs (gauche).
            var list = UiFactory.Panel("Players", root, tl, tl, new Vector2(16, -16), new Vector2(380, 74 + Rows * 66), UiFactory.PanelColor);
            UiFactory.Label("Header", list.transform, "EN LICE", 28, TextAnchor.MiddleLeft, UiFactory.Accent, tl, tl, new Vector2(20, -10), new Vector2(340, 44));
            _listRoot = list.rectTransform;
            for (int i = 0; i < Rows; i++)
            {
                var r = new Row();
                r.root = UiFactory.Rect("Row" + i, list.transform, tl, tl, tl, new Vector2(12, -62 - i * 66), new Vector2(356, 60));
                r.avatar = UiFactory.Picture("Avatar", r.root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, 54);
                r.name = UiFactory.Label("Name", r.root, "", 25, TextAnchor.UpperLeft, Color.white, tl, tl, new Vector2(64, -2), new Vector2(230, 32));
                r.level = UiFactory.Label("Lvl", r.root, "", 22, TextAnchor.UpperRight, UiFactory.Gold, tr, tr, new Vector2(-4, -3), new Vector2(70, 30));
                r.bar = UiFactory.Bar("Energy", r.root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(64, 6), new Vector2(286, 14), UiFactory.Accent);
                _rows.Add(r);
            }

            // Annonces des cadeaux (image + pseudo + action), sous le bandeau du haut.
            var gp = UiFactory.Panel("GiftPopup", root, top, top, new Vector2(0, -140), new Vector2(620, 110), new Color(0.06f, 0.04f, 0.14f, 0.9f));
            _giftCard = gp.rectTransform;
            _giftGroup = gp.gameObject.AddComponent<CanvasGroup>();
            _giftIcon = UiFactory.Picture("Icon", gp.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10, 0), 96);
            _giftName = UiFactory.Label("Name", gp.transform, "", 30, TextAnchor.MiddleLeft, Color.white, tl, tl, new Vector2(116, -8), new Vector2(490, 46));
            _giftEffect = UiFactory.Label("Effect", gp.transform, "", 30, TextAnchor.MiddleLeft, UiFactory.Gold, tl, tl, new Vector2(116, -56), new Vector2(490, 46));
            _giftGroup.alpha = 0f;

            // Bas : invitation + état.
            var hintPanel = _hintPanel = UiFactory.Panel("Hint", root, bottom, bottom, new Vector2(0, 16), new Vector2(980, 66), UiFactory.PanelColor);
            _hint = UiFactory.Label("HintText", hintPanel.transform, "Écris <color=#FFD54A>!join</color> dans le chat pour entrer dans l'arène", 32, TextAnchor.MiddleCenter, Color.white, center, center, Vector2.zero, new Vector2(960, 60));
            _status = UiFactory.Label("Status", root, "", 18, TextAnchor.LowerLeft, new Color(1f, 1f, 1f, 0.6f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18, 10), new Vector2(560, 60));
            _demoTag = UiFactory.Label("Demo", root, "MODE DÉMO", 30, TextAnchor.LowerRight, new Color(1f, 0.35f, 0.3f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20, 14), new Vector2(300, 44));
            _demoTag.gameObject.SetActive(false);

            // Annonce centrale.
            var bannerRt = UiFactory.Rect("Banner", root, center, center, center, new Vector2(0, 120), new Vector2(1200, 260));
            _bannerGroup = bannerRt.gameObject.AddComponent<CanvasGroup>();
            _banner = UiFactory.Label("Big", bannerRt, "", 110, TextAnchor.MiddleCenter, Color.white, center, center, new Vector2(0, 30), new Vector2(1200, 150));
            _bannerSub = UiFactory.Label("Sub", bannerRt, "", 40, TextAnchor.MiddleCenter, UiFactory.Gold, center, center, new Vector2(0, -70), new Vector2(1200, 60));
            _bannerGroup.alpha = 0f;

            // Carte d'entrée dans l'arène.
            var join = UiFactory.Panel("JoinCard", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 100), new Vector2(620, 120), new Color(0.05f, 0.1f, 0.25f, 0.92f));
            _joinCard = join.rectTransform;
            _joinGroup = join.gameObject.AddComponent<CanvasGroup>();
            _joinAvatar = UiFactory.Picture("Avatar", join.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12, 0), 96);
            _joinName = UiFactory.Label("Name", join.transform, "", 34, TextAnchor.MiddleLeft, Color.white, tl, tl, new Vector2(122, -10), new Vector2(480, 50));
            _joinTop = UiFactory.Label("Top", join.transform, "", 26, TextAnchor.MiddleLeft, UiFactory.Gold, tl, tl, new Vector2(122, -62), new Vector2(480, 44));
            _joinGroup.alpha = 0f;

            // Classement (!top).
            var board = UiFactory.Panel("Board", root, center, center, new Vector2(0, -40), new Vector2(620, 420), new Color(0.04f, 0.06f, 0.16f, 0.93f));
            _boardCard = board.rectTransform;
            _boardGroup = board.gameObject.AddComponent<CanvasGroup>();
            UiFactory.Label("Title", board.transform, "CLASSEMENT", 40, TextAnchor.MiddleCenter, UiFactory.Gold, top, top, new Vector2(0, -12), new Vector2(600, 56));
            _boardText = UiFactory.Label("Lines", board.transform, "", 30, TextAnchor.UpperLeft, Color.white, top, top, new Vector2(0, -80), new Vector2(560, 330));
            _boardGroup.alpha = 0f;

            // Écran du vainqueur.
            var win = UiFactory.Panel("Winner", root, center, center, new Vector2(0, -30), new Vector2(760, 470), new Color(0.06f, 0.05f, 0.14f, 0.94f));
            _winnerCard = win.rectTransform;
            _winnerGroup = win.gameObject.AddComponent<CanvasGroup>();
            UiFactory.Label("Crown", win.transform, "CHAMPION", 54, TextAnchor.MiddleCenter, UiFactory.Gold, top, top, new Vector2(0, -16), new Vector2(740, 70));
            _winnerAvatar = UiFactory.Picture("Avatar", win.transform, top, top, new Vector2(0, -96), 200);
            _winnerName = UiFactory.Label("Name", win.transform, "", 50, TextAnchor.MiddleCenter, Color.white, top, top, new Vector2(0, -306), new Vector2(740, 64));
            _winnerSub = UiFactory.Label("Sub", win.transform, "", 28, TextAnchor.MiddleCenter, UiFactory.Accent, top, top, new Vector2(0, -372), new Vector2(740, 80));
            _winnerGroup.alpha = 0f;
        }

        // ------------------------------------------------------------------ API

        public void SetPhase(RoundPhase phase, float remaining, int round, int alive, int total, int queued)
        {
            switch (phase)
            {
                case RoundPhase.Lobby: _phase.text = "MANCHE " + round + " · INSCRIPTIONS"; break;
                case RoundPhase.Countdown: _phase.text = "MANCHE " + round + " · PRÉPAREZ-VOUS"; break;
                case RoundPhase.Fighting: _phase.text = "MANCHE " + round + " · COMBAT"; break;
                default: _phase.text = "MANCHE " + round + " · VICTOIRE"; break;
            }
            int s = Mathf.CeilToInt(remaining);
            _timer.text = phase == RoundPhase.Lobby && total < 2 ? "--:--" : (s / 60) + ":" + (s % 60).ToString("00");
            _survivors.text = phase == RoundPhase.Fighting || phase == RoundPhase.Victory
                ? "SURVIVANTS " + alive + "/" + total
                : "INSCRITS " + total + (queued > 0 ? " (+" + queued + ")" : "");
            _hint.text = phase == RoundPhase.Fighting
                ? "Tape l'écran pour booster et faire grossir ta toupie !"
                : "Écris <color=#FFD54A>!join</color> dans le chat pour entrer dans l'arène";
        }

        public void SetStatus(string text, bool demo)
        {
            _status.text = text;
            _demoTag.gameObject.SetActive(demo);
        }

        /// <summary>Met à jour la liste de gauche (meilleures énergies d'abord).</summary>
        public void SetPlayers(IList<TopController> tops, System.Func<string, int> levelOf)
        {
            for (int i = 0; i < Rows; i++)
            {
                var r = _rows[i];
                if (i < tops.Count)
                {
                    var t = tops[i];
                    r.root.gameObject.SetActive(true);
                    r.name.text = t.DisplayName;
                    r.level.text = "Nv " + levelOf(t.PlayerId);
                    UiFactory.SetBar(r.bar, t.SpinFraction);
                    r.bar.color = t.SpinFraction < 0.25f ? new Color(1f, 0.35f, 0.3f) : t.Glow;
                    Texture tex = AvatarCache.Instance.Get(t.PlayerId, null, null);
                    if (_avatarOverride.TryGetValue(t.PlayerId, out var photo) && photo != null) tex = photo;
                    r.avatar.texture = tex;
                }
                else r.root.gameObject.SetActive(false);
            }
        }

        readonly Dictionary<string, Texture> _avatarOverride = new Dictionary<string, Texture>();
        /// <summary>Photo de profil connue d'un joueur (sinon l'avatar généré est utilisé).</summary>
        public void SetAvatar(string playerId, Texture tex) { if (tex != null) _avatarOverride[playerId] = tex; }
        public Texture AvatarOf(string playerId) =>
            _avatarOverride.TryGetValue(playerId, out var t) && t != null ? t : AvatarCache.Instance.Generated(playerId);

        /// <summary>Annonce d'un cadeau : image, pseudo et action (sans montant).</summary>
        public void GiftPopup(string user, Texture icon, string effect)
        {
            if (_giftQueue.Count > 8) _giftQueue.Dequeue();
            _giftQueue.Enqueue((user, icon, effect));
        }

        public void Banner(string big, string sub, float seconds, Color? color = null)
        {
            _banner.text = big;
            _banner.color = color ?? Color.white;
            _bannerSub.text = sub ?? "";
            _bannerUntil = Time.unscaledTime + seconds;
            _bannerGroup.alpha = 1f;
            _banner.rectTransform.localScale = Vector3.one * 1.35f;
        }

        public void JoinCard(string name, Texture avatar, TopDefinition top)
        {
            if (_joinQueue.Count > 6) return; // en cas d'afflux, le fil et la liste suffisent
            _joinQueue.Enqueue((name, avatar, top != null ? top.name + " · " + TopDefinition.RarityName(top.rarity) : "", Compat.Hex(top != null ? TopDefinition.RarityColor(top.rarity) : "#FFFFFF")));
        }

        public void ShowLeaderboard(List<PlayerProfile> top)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < top.Count; i++)
            {
                var p = top[i];
                string medal = i == 0 ? "<color=#FFD54A>1.</color>" : i == 1 ? "<color=#D6DEE8>2.</color>" : i == 2 ? "<color=#E39A5B>3.</color>" : (i + 1) + ".";
                sb.AppendLine(medal + " " + p.DisplayName + "  <color=#5CE1FF>" + p.wins + " V</color> · Nv " + p.level);
            }
            if (top.Count == 0) sb.Append("Pas encore de champion : à vous de jouer !");
            _boardText.text = sb.ToString();
            _boardUntil = Time.unscaledTime + 8f;
        }

        public void ShowWinner(string name, Texture avatar, string details)
        {
            _winnerName.text = name;
            _winnerAvatar.texture = avatar;
            _winnerSub.text = details;
            _winnerVisible = true;
            _winnerCard.localScale = Vector3.one * 0.6f;
        }

        public void HideWinner() => _winnerVisible = false;

        // ------------------------------------------------------------------ animations

        void Update()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            var disp = GameConfig.Current.display;
            _listRoot.gameObject.SetActive(disp.showPlayerList);
            _hintPanel.gameObject.SetActive(disp.showHint);
            _status.gameObject.SetActive(disp.showStatus);
            _banner.transform.parent.gameObject.SetActive(disp.showBanners);
            if (!disp.showGiftPopups) { _giftQueue.Clear(); _giftUntil = 0f; }
            if (!disp.showJoinCards) { _joinQueue.Clear(); _joinUntil = 0f; }

            if (_bannerGroup.alpha > 0f)
            {
                var s = _banner.rectTransform.localScale.x;
                _banner.rectTransform.localScale = Vector3.one * Mathf.Lerp(s, 1f, 1f - Mathf.Exp(-dt * 10f));
                if (t > _bannerUntil) _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, 0f, dt * 3f);
            }

            if (t > _joinUntil && _joinQueue.Count > 0)
            {
                var j = _joinQueue.Dequeue();
                _joinName.text = j.name + " <color=#9FEFFF>entre dans l'arène !</color>";
                _joinTop.text = j.top;
                _joinTop.color = j.rc;
                _joinAvatar.texture = j.tex;
                _joinUntil = t + (_joinQueue.Count > 2 ? 1.2f : 2.4f);
                _joinCard.anchoredPosition = new Vector2(-700, 100);
            }
            bool joinOn = t < _joinUntil;
            _joinGroup.alpha = Mathf.MoveTowards(_joinGroup.alpha, joinOn ? 1f : 0f, dt * 5f);
            _joinCard.anchoredPosition = Vector2.Lerp(_joinCard.anchoredPosition, new Vector2(0, 100), 1f - Mathf.Exp(-dt * 12f));

            _boardGroup.alpha = Mathf.MoveTowards(_boardGroup.alpha, t < _boardUntil && !_winnerVisible ? 1f : 0f, dt * 4f);

            _winnerGroup.alpha = Mathf.MoveTowards(_winnerGroup.alpha, _winnerVisible ? 1f : 0f, dt * 3f);
            _winnerCard.localScale = Vector3.one * Mathf.Lerp(_winnerCard.localScale.x, _winnerVisible ? 1f : 0.8f, 1f - Mathf.Exp(-dt * 8f));

            if (t > _giftUntil && _giftQueue.Count > 0)
            {
                var g = _giftQueue.Dequeue();
                _giftName.text = g.user;
                _giftEffect.text = g.effect;
                _giftIcon.texture = g.icon;
                _giftIcon.gameObject.SetActive(g.icon != null);
                _giftUntil = t + (_giftQueue.Count > 2 ? 1.1f : 2.2f);
                _giftCard.localScale = Vector3.one * 1.25f;
            }
            _giftGroup.alpha = Mathf.MoveTowards(_giftGroup.alpha, t < _giftUntil ? 1f : 0f, dt * 5f);
            _giftCard.localScale = Vector3.one * Mathf.Lerp(_giftCard.localScale.x, 1f, 1f - Mathf.Exp(-dt * 10f));
        }
    }
}
