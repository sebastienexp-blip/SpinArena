using System.Collections.Generic;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;
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
        Text _boardTitle, _announce, _taps;
        CanvasGroup _announceGroup;
        float _announceUntil;
        readonly Queue<(string text, Color color)> _announceQueue = new Queue<(string, Color)>();
        public bool TimerWarning { get; set; }
        RectTransform _tagsRoot, _legendRoot;
        sealed class Tag { public RectTransform root; public Image bg; public Text text; public string name; }
        readonly List<Tag> _tags = new List<Tag>();
        RectTransform _giftCard;
        CanvasGroup _giftGroup;
        RawImage _giftIcon;
        Text _giftName, _giftEffect;
        float _giftUntil;
        readonly Queue<(string user, Texture icon, string effect)> _giftQueue = new Queue<(string, Texture, string)>();
        const int Rows = 10;

        RectTransform _hudRoot;
        CanvasScaler _scaler;
        bool _portrait;
        Vector2 _joinPos = new Vector2(0, 100);
        /// <summary>Vrai quand l'écran est plus haut que large (live TikTok en vertical).</summary>
        public bool Portrait => _portrait;

        void Awake()
        {
            Instance = this;
            var cgo = new GameObject("HUD", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            Canvas = cgo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            _scaler = cgo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.matchWidthOrHeight = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            BuildLayout(Screen.height > Screen.width);
        }

        /// <summary>
        /// Construit l'interface pour le sens actuel de l'écran (paysage 1920×1080 ou portrait 1080×1920).
        /// Le menu des options reste sur le même canevas : seule la couche du direct est reconstruite.
        /// </summary>
        void BuildLayout(bool portrait)
        {
            _portrait = portrait;
            _scaler.referenceResolution = portrait ? new Vector2(1080, 1920) : new Vector2(1920, 1080);
            // Les téléphones récents sont plus allongés que 16:9 (S23 Ultra : 1080×2316). On cale l'interface sur
            // la largeur en portrait et sur la hauteur en paysage : rien n'est coupé sur les bords.
            _scaler.matchWidthOrHeight = portrait ? 0f : 1f;
            if (_hudRoot != null) Destroy(_hudRoot.gameObject);
            if (_tagsRoot != null) Destroy(_tagsRoot.gameObject);
            _rows.Clear();
            _tags.Clear();
            _hudRoot = UiFactory.Rect("Live", Canvas.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _hudRoot.SetAsFirstSibling();
            _safe = default;
            ApplySafeArea();
            Build(_hudRoot);
        }

        Rect _safe;
        /// <summary>
        /// Garde l'interface dans la zone sûre de l'écran (hors trou de la caméra et coins arrondis),
        /// avec une petite marge en plus pour que rien ne touche les bords.
        /// </summary>
        bool ApplySafeArea()
        {
            Rect sa = Screen.safeArea;
            if (sa == _safe || Screen.width <= 0 || Screen.height <= 0) return false;
            _safe = sa;
            float m = Mathf.Min(Screen.width, Screen.height) * 0.012f;
            _hudRoot.anchorMin = new Vector2((sa.xMin + m) / Screen.width, (sa.yMin + m) / Screen.height);
            _hudRoot.anchorMax = new Vector2((sa.xMax - m) / Screen.width, (sa.yMax - m) / Screen.height);
            _hudRoot.offsetMin = _hudRoot.offsetMax = Vector2.zero;
            return true;
        }

        // ------------------------------------------------------------------ cadeaux → actions (portrait, à droite)

        sealed class GiftRow { public RectTransform root; public RawImage icon; public Text count, action; public Image badge; }
        readonly List<GiftRow> _giftRows = new List<GiftRow>();
        readonly List<(Texture icon, string slug, string action)> _giftList = new List<(Texture, string, string)>();
        RectTransform _giftPanel;
        CanvasGroup _giftPanelGroup;
        int _giftPage, _giftVersion = -1;
        float _giftPageTimer, _giftCountTimer;
        const int GiftRowsShown = 5;

        /// <summary>
        /// Panneau de droite : l'image de chaque cadeau, combien ont été reçus pendant le live, et ce qu'il déclenche.
        /// 5 lignes à la fois ; il fait défiler toute la liste tout seul.
        /// </summary>
        void BuildGiftPanel(Transform root)
        {
            _giftRows.Clear();
            _giftVersion = -1;
            _giftPage = 0;
            _giftPageTimer = 5f;
            Vector2 tr = new Vector2(1f, 1f), tl = new Vector2(0f, 1f);
            var panel = UiFactory.Panel("GiftActions", root, tr, tr, new Vector2(-8, -236), new Vector2(330, 24 + GiftRowsShown * 92), new Color(0.05f, 0.05f, 0.12f, 0.55f));
            _giftPanel = panel.rectTransform;
            _giftPanelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            for (int i = 0; i < GiftRowsShown; i++)
            {
                var r = new GiftRow();
                r.root = UiFactory.Rect("Gift" + i, panel.transform, tl, tl, tl, new Vector2(10, -12 - i * 92), new Vector2(310, 88));
                r.icon = UiFactory.Picture("Icon", r.root, tl, tl, new Vector2(2, 0), 66);
                r.badge = UiFactory.Panel("Badge", r.root, tl, tl, new Vector2(4, -60), new Vector2(62, 26), new Color(0f, 0f, 0f, 0.65f));
                r.count = UiFactory.Label("Count", r.badge.transform, "0", 20, TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62, 26));
                r.count.fontStyle = FontStyle.Bold;
                r.action = UiFactory.Label("Action", r.root, "", 27, TextAnchor.MiddleLeft, Color.white, tl, tl, new Vector2(80, -14), new Vector2(226, 44));
                r.action.fontStyle = FontStyle.Bold;
                _giftRows.Add(r);
            }
            RefreshGiftList();
        }

        void RefreshGiftList()
        {
            _giftList.Clear();
            var seen = new HashSet<string>();
            foreach (var r in GameConfig.Current.gifts.rules)
            {
                if (string.IsNullOrEmpty(r.giftName)) continue;
                var icon = Live.GiftIcons.Get(r.giftName);
                string slug = Live.GiftIcons.Slug(r.giftName == "Donut" ? "Doughnut" : r.giftName);
                if (icon == null || !seen.Add(slug)) continue;
                _giftList.Add((icon, slug, GiftEffects.NameOf(r.effect)));
            }
            _giftVersion = -1;
            ShowGiftPage();
        }

        void ShowGiftPage()
        {
            if (_giftPanel == null) return;
            int pages = Mathf.Max(1, Mathf.CeilToInt(_giftList.Count / (float)GiftRowsShown));
            _giftPage %= pages;
            var counts = GameManager.Instance != null && GameManager.Instance.Gifts != null ? GameManager.Instance.Gifts.ReceivedCounts : null;
            int shown = 0;
            for (int i = 0; i < _giftRows.Count; i++)
            {
                int idx = _giftPage * GiftRowsShown + i;
                var r = _giftRows[i];
                bool on = idx < _giftList.Count;
                r.root.gameObject.SetActive(on);
                if (!on) continue;
                shown++;
                var g = _giftList[idx];
                r.icon.texture = g.icon;
                int c = 0;
                if (counts != null) counts.TryGetValue(g.slug, out c);
                r.count.text = c > 999 ? (c / 1000f).ToString("0.#") + "k" : c.ToString();
                r.action.text = g.action;
            }
            _giftPanel.sizeDelta = new Vector2(330, 24 + shown * 92);
        }

        void UpdateGiftPanel(float dt)
        {
            if (_giftPanel == null || !_portrait) return;
            bool show = GameConfig.Current.display.showDonors && _giftList.Count > 0;
            _giftPanel.gameObject.SetActive(show);
            if (!show) return;
            // Défilement : nouvelle page toutes les 5 s, avec un fondu.
            _giftPageTimer -= dt;
            if (_giftPageTimer <= 0f) { _giftPageTimer = 5f; _giftPage++; ShowGiftPage(); _giftPanelGroup.alpha = 0f; }
            _giftPanelGroup.alpha = Mathf.MoveTowards(_giftPanelGroup.alpha, 1f, dt * 4f);
            // Compteurs mis à jour quand un cadeau arrive.
            _giftCountTimer -= dt;
            if (_giftCountTimer > 0f) return;
            _giftCountTimer = 0.4f;
            var gifts = GameManager.Instance != null ? GameManager.Instance.Gifts : null;
            if (gifts != null && gifts.ReceivedVersion != _giftVersion) { _giftVersion = gifts.ReceivedVersion; ShowGiftPage(); }
        }

        void Build(Transform root)
        {
            Vector2 top = new Vector2(0.5f, 1f), tl = new Vector2(0f, 1f), tr = new Vector2(1f, 1f), bottom = new Vector2(0.5f, 0f), center = new Vector2(0.5f, 0.5f);
            bool P = _portrait;
            // Largeur utile réelle (zone sûre comprise) : en portrait le canevas fait 1080 de large.
            float W = P ? 1080f * (_hudRoot.anchorMax.x - _hudRoot.anchorMin.x) : 1920f;

            // Pseudos des toupies (sous tous les panneaux), sur tout l'écran : ils suivent les toupies, pas la zone sûre.
            _tagsRoot = UiFactory.Rect("NameTags", Canvas.transform, Vector2.zero, Vector2.one, center, Vector2.zero, Vector2.zero);
            _tagsRoot.SetAsFirstSibling();

            // Légende des cadeaux : à droite en paysage, en bas sur deux colonnes en portrait.
            var legend = P
                ? UiFactory.Panel("GiftLegend", root, bottom, bottom, new Vector2(0, 34), new Vector2(W - 24, 480), UiFactory.PanelColor)
                : UiFactory.Panel("GiftLegend", root, tr, tr, new Vector2(-14, -14), new Vector2(440, 1000), UiFactory.PanelColor);
            _legendRoot = legend.rectTransform;
            RefreshLegend();

            if (P)
            {
                // Portrait épuré : petit titre, gros chrono dans un cadre lumineux, la manche et les survivants dessous.
                UiFactory.Label("Title", root, "<color=#5CE1FF>SPIN</color> ARENA", 34, TextAnchor.MiddleCenter, Color.white, top, top, new Vector2(0, -2), new Vector2(600, 44));
                var frame = UiFactory.Panel("TimerFrame", root, top, top, new Vector2(0, -46), new Vector2(300, 108), new Color(1f, 0.25f, 0.35f, 0.95f));
                var inner = UiFactory.Panel("TimerInner", frame.transform, center, center, Vector2.zero, new Vector2(288, 96), new Color(0.03f, 0.03f, 0.1f, 0.96f));
                _timer = UiFactory.Label("Timer", inner.transform, "", 70, TextAnchor.MiddleCenter, new Color(1f, 0.72f, 0.25f), center, center, Vector2.zero, new Vector2(280, 96));
                _timer.fontStyle = FontStyle.Bold;
                _phase = UiFactory.Label("Phase", root, "", 26, TextAnchor.MiddleCenter, UiFactory.Accent, top, top, new Vector2(0, -160), new Vector2(W - 40, 36));
                _survivors = UiFactory.Label("Survivors", root, "", 26, TextAnchor.MiddleCenter, UiFactory.Gold, top, top, new Vector2(0, -194), new Vector2(W - 40, 36));
                _taps = UiFactory.Label("Taps", root, "", 20, TextAnchor.UpperRight, new Color(1f, 0.6f, 0.75f), tr, tr, new Vector2(-8, -6), new Vector2(260, 30));
                BuildGiftPanel(root);
            }
            else
            {
                // Bandeau du haut.
                var bar = UiFactory.Panel("TopBar", root, top, top, new Vector2(0, -14), new Vector2(760, 112), UiFactory.PanelColor);
                UiFactory.Label("Title", bar.transform, "<color=#5CE1FF>SPIN</color> ARENA", 50, TextAnchor.MiddleCenter, Color.white, top, top, new Vector2(0, -6), new Vector2(740, 60));
                _phase = UiFactory.Label("Phase", bar.transform, "", 28, TextAnchor.MiddleLeft, UiFactory.Accent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(26, 8), new Vector2(320, 40));
                _timer = UiFactory.Label("Timer", bar.transform, "", 30, TextAnchor.MiddleCenter, Color.white, bottom, bottom, new Vector2(0, 8), new Vector2(160, 40));
                _taps = UiFactory.Label("Taps", bar.transform, "", 20, TextAnchor.UpperRight, new Color(1f, 0.6f, 0.75f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18, -8), new Vector2(240, 30));
                _survivors = UiFactory.Label("Survivors", bar.transform, "", 28, TextAnchor.MiddleRight, UiFactory.Gold, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-26, 8), new Vector2(320, 40));
            }

            // Liste des joueurs : à gauche en paysage (10 lignes), sous le bandeau en portrait (6 joueurs sur 2 colonnes).
            int rows = P ? 6 : Rows;
            var list = P
                ? UiFactory.Panel("Players", root, top, top, new Vector2(0, -136), new Vector2(W - 24, 56 + 3 * 64), UiFactory.PanelColor)
                : UiFactory.Panel("Players", root, tl, tl, new Vector2(16, -16), new Vector2(380, 74 + Rows * 66), UiFactory.PanelColor);
            UiFactory.Label("Header", list.transform, "EN LICE", P ? 26 : 28, TextAnchor.MiddleLeft, UiFactory.Accent, tl, tl, new Vector2(20, P ? -4 : -10), new Vector2(340, 44));
            _listRoot = list.rectTransform;
            for (int i = 0; i < rows; i++)
            {
                var r = new Row();
                Vector2 pos = P ? new Vector2(12 + (i % 2) * ((W - 24) / 2f), -50 - (i / 2) * 64) : new Vector2(12, -62 - i * 66);
                float rw = P ? (W - 24) / 2f - 24 : 356;
                r.root = UiFactory.Rect("Row" + i, list.transform, tl, tl, tl, pos, new Vector2(rw, 60));
                r.avatar = UiFactory.Picture("Avatar", r.root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, 54);
                r.name = UiFactory.Label("Name", r.root, "", 25, TextAnchor.UpperLeft, Color.white, tl, tl, new Vector2(64, -2), new Vector2(rw - 130, 32));
                r.level = UiFactory.Label("Lvl", r.root, "", 22, TextAnchor.UpperRight, UiFactory.Gold, tr, tr, new Vector2(-4, -3), new Vector2(70, 30));
                r.bar = UiFactory.Bar("Energy", r.root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(64, 6), new Vector2(rw - 70, 14), UiFactory.Accent);
                _rows.Add(r);
            }

            // Annonces des cadeaux (image + pseudo + action), sous le bandeau du haut (sous la liste en portrait).
            var gp = P
                ? UiFactory.Panel("GiftPopup", root, tl, tl, new Vector2(12, -240), new Vector2(620, 110), new Color(0.06f, 0.04f, 0.14f, 0.9f))
                : UiFactory.Panel("GiftPopup", root, top, top, new Vector2(0, -140), new Vector2(620, 110), new Color(0.06f, 0.04f, 0.14f, 0.9f));
            _giftCard = gp.rectTransform;
            _giftGroup = gp.gameObject.AddComponent<CanvasGroup>();
            _giftIcon = UiFactory.Picture("Icon", gp.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10, 0), 96);
            _giftName = UiFactory.Label("Name", gp.transform, "", 30, TextAnchor.MiddleLeft, Color.white, tl, tl, new Vector2(116, -8), new Vector2(490, 46));
            _giftEffect = UiFactory.Label("Effect", gp.transform, "", 30, TextAnchor.MiddleLeft, UiFactory.Gold, tl, tl, new Vector2(116, -56), new Vector2(490, 46));
            _giftGroup.alpha = 0f;

            // Bas : invitation + état (au-dessus de la liste des cadeaux en portrait).
            float hintW = P ? W - 40 : 980;
            var hintPanel = _hintPanel = UiFactory.Panel("Hint", root, bottom, bottom, new Vector2(0, P ? 526 : 16), new Vector2(hintW, 66), UiFactory.PanelColor);
            _hint = UiFactory.Label("HintText", hintPanel.transform, "Viens dans le live : ta toupie entre <color=#FFD54A>automatiquement</color> !", P ? 30 : 32, TextAnchor.MiddleCenter, Color.white, center, center, Vector2.zero, new Vector2(hintW - 20, 60));
            _status = UiFactory.Label("Status", root, "", P ? 16 : 18, TextAnchor.LowerLeft, new Color(1f, 1f, 1f, 0.6f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18, P ? 4 : 10), new Vector2(P ? 760 : 560, P ? 28 : 60));
            _demoTag = P
                ? UiFactory.Label("Demo", root, "MODE DÉMO", 24, TextAnchor.LowerRight, new Color(1f, 0.35f, 0.3f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18, 2), new Vector2(260, 30))
                : UiFactory.Label("Demo", root, "MODE DÉMO", 30, TextAnchor.LowerLeft, new Color(1f, 0.35f, 0.3f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18, 64), new Vector2(300, 44));
            _demoTag.gameObject.SetActive(false);

            // Annonce centrale.
            float bannerW = P ? W - 40 : 1200;
            var bannerRt = UiFactory.Rect("Banner", root, center, center, center, new Vector2(0, P ? 60 : 120), new Vector2(bannerW, 260));
            _bannerGroup = bannerRt.gameObject.AddComponent<CanvasGroup>();
            _banner = UiFactory.Label("Big", bannerRt, "", P ? 100 : 110, TextAnchor.MiddleCenter, Color.white, center, center, new Vector2(0, 30), new Vector2(bannerW, 150));
            _bannerSub = UiFactory.Label("Sub", bannerRt, "", P ? 36 : 40, TextAnchor.MiddleCenter, UiFactory.Gold, center, center, new Vector2(0, -70), new Vector2(bannerW, 60));
            _bannerGroup.alpha = 0f;

            // Annonces moyennes (séries, primes, duel…), sous l'annonce des cadeaux.
            float annW = P ? W - 40 : 1000;
            var ann = UiFactory.Rect("Announce", root, top, top, top, new Vector2(0, P ? -760 : -262), new Vector2(annW, 70));
            _announceGroup = ann.gameObject.AddComponent<CanvasGroup>();
            _announce = UiFactory.Label("Text", ann, "", P ? 40 : 44, TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(annW, 70));
            _announce.fontStyle = FontStyle.Bold;
            _announceGroup.alpha = 0f;

            // Carte d'entrée dans l'arène.
            // Portrait : sous l'annonce des cadeaux, en haut à gauche (le bas de l'écran est couvert par le chat TikTok).
            Vector2 joinAnchor = P ? tl : new Vector2(0.5f, 0f);
            _joinPos = P ? new Vector2(12, -364) : new Vector2(0, 100);
            var join = UiFactory.Panel("JoinCard", root, joinAnchor, joinAnchor, _joinPos, new Vector2(620, 120), new Color(0.05f, 0.1f, 0.25f, 0.92f));
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
            _boardTitle = UiFactory.Label("Title", board.transform, "CLASSEMENT", 40, TextAnchor.MiddleCenter, UiFactory.Gold, top, top, new Vector2(0, -12), new Vector2(600, 56));
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
                ? "Tape l'écran pour booster ta toupie · cadeaux = pouvoirs"
                : "Viens dans le live : ta toupie entre <color=#FFD54A>automatiquement</color> !";
        }

        public void SetStatus(string text, bool demo)
        {
            _status.text = text;
            _demoTag.gameObject.SetActive(demo);
        }

        /// <summary>Met à jour la liste de gauche (meilleures énergies d'abord).</summary>
        readonly HashSet<string> _tagAllowed = new HashSet<string>();
        int _aliveForTags;

        public void SetPlayers(IList<TopController> tops, System.Func<string, int> levelOf)
        {
            // Avec des centaines de toupies, seuls les pseudos des plus en forme (et du roi / des primes) restent affichés.
            _aliveForTags = tops.Count;
            _tagAllowed.Clear();
            int cap = GameConfig.Current.display.maxNameTags;
            for (int i = 0; i < tops.Count; i++)
                if (cap <= 0 || i < cap || tops[i].Crowned || tops[i].HasBounty) _tagAllowed.Add(tops[i].PlayerId);
            for (int i = 0; i < _rows.Count; i++)
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

        /// <summary>
        /// Légende à droite : chaque cadeau configuré avec son image, son nom et son action, plus les tapotements.
        /// Reconstruite à partir des règles actuelles (elle suit les changements faits dans les options).
        /// </summary>
        public void RefreshLegend()
        {
            if (_giftPanel != null) RefreshGiftList();
            if (_legendRoot == null) return;
            for (int i = _legendRoot.childCount - 1; i >= 0; i--) Destroy(_legendRoot.GetChild(i).gameObject);
            var cfg = GameConfig.Current;
            Vector2 tl = new Vector2(0f, 1f);
            UiFactory.Label("Title", _legendRoot, "INTERACTIONS", 28, TextAnchor.MiddleLeft, UiFactory.Accent, tl, tl, new Vector2(18, -8), new Vector2(400, 44));
            var rows = new List<(Texture icon, string name, string action)>();
            rows.Add((null, "Tapotements", "+" + (cfg.effects.tapGrowth * 100f).ToString("0.#") + " % taille, vitesse"));
            var seen = new HashSet<string>();
            foreach (var r in cfg.gifts.rules)
            {
                if (string.IsNullOrEmpty(r.giftName)) continue;
                var icon = Live.GiftIcons.Get(r.giftName);
                if (icon == null || !seen.Add(Live.GiftIcons.Slug(r.giftName == "Donut" ? "Doughnut" : r.giftName))) continue; // un seul « Doughnut »
                rows.Add((icon, r.giftName, GiftEffects.NameOf(r.effect)));
            }
            // Paysage : une colonne à droite. Portrait : deux colonnes dans le panneau du bas.
            int cols = _portrait ? 2 : 1;
            float colW = _portrait ? (_legendRoot.sizeDelta.x - 24f) / 2f : 430f;
            int perCol = Mathf.CeilToInt(rows.Count / (float)cols);
            float avail = _legendRoot.sizeDelta.y - 66f;
            float rowH = Mathf.Min(42f, avail / Mathf.Max(1, perCol));
            int fs = _portrait ? 19 : 20;
            for (int i = 0; i < rows.Count; i++)
            {
                int col = i / perCol, line = i % perCol;
                float x0 = 12 + col * colW;
                float y = -56 - line * rowH;
                var (icon, name, action) = rows[i];
                if (icon != null)
                {
                    var pic = UiFactory.Picture("Icon", _legendRoot, tl, new Vector2(0f, 0.5f), new Vector2(x0, y - rowH * 0.5f), rowH - 4);
                    pic.texture = icon;
                }
                else
                {
                    UiFactory.Label("Tap", _legendRoot, "x15", 18, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.75f), tl, new Vector2(0f, 0.5f), new Vector2(x0 - 2, y - rowH * 0.5f), new Vector2(rowH, rowH));
                }
                float nameW = colW * 0.38f, actW = colW - nameW - 60f;
                UiFactory.Label("Name", _legendRoot, name, fs, TextAnchor.MiddleLeft, Color.white, tl, new Vector2(0f, 0.5f), new Vector2(x0 + 48, y - rowH * 0.5f), new Vector2(nameW, rowH));
                UiFactory.Label("Action", _legendRoot, action, fs, TextAnchor.MiddleRight, UiFactory.Gold, tl, new Vector2(0f, 0.5f), new Vector2(x0 + 48 + nameW, y - rowH * 0.5f), new Vector2(actW, rowH));
            }
        }

        /// <summary>Pseudos au-dessus des toupies, en texte d'interface net avec contour et fond sombre.</summary>
        void LateUpdate()
        {
            var gm = GameManager.Instance;
            var cam = Camera.main;
            var disp = GameConfig.Current.display;
            int used = 0;
            if (gm != null && cam != null && disp.showNames && gm.Battle != null)
            {
                var canvasRt = (RectTransform)Canvas.transform;
                float scale = Mathf.Clamp(disp.nameSize, 0.5f, 2f) * Mathf.Clamp(Mathf.Sqrt(45f / Mathf.Max(1, _aliveForTags)), 0.7f, 1f);
                bool limited = disp.maxNameTags > 0 && _aliveForTags > disp.maxNameTags;
                foreach (var top in gm.Battle.Tops.Values)
                {
                    if (!top.Alive || !top.gameObject.activeInHierarchy) continue;
                    if (limited && !_tagAllowed.Contains(top.PlayerId)) continue;
                    Vector3 sp = cam.WorldToScreenPoint(top.NameAnchor);
                    if (sp.z <= 0f) continue;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var local)) continue;
                    if (used >= _tags.Count) _tags.Add(NewTag());
                    var tag = _tags[used++];
                    if (!tag.root.gameObject.activeSelf) tag.root.gameObject.SetActive(true);
                    string n = top.ShortName;
                    if (tag.name != n)
                    {
                        tag.name = n;
                        tag.text.text = n;
                    }
                    float w = tag.text.preferredWidth + 22f;
                    tag.root.sizeDelta = new Vector2(w, 40f);
                    tag.root.localScale = Vector3.one * scale;
                    tag.root.anchoredPosition = local;
                    bool low = top.SpinFraction < 0.25f;
                    string shown = top.HasBounty ? n + "  PRIME" : n;
                    if (tag.text.text != shown) { tag.text.text = shown; w = tag.text.preferredWidth + 22f; tag.root.sizeDelta = new Vector2(w, 40f); }
                    tag.text.color = top.Crowned ? new Color(1f, 0.85f, 0.35f) : low ? new Color(1f, 0.55f, 0.5f) : Color.white;
                    tag.bg.color = top.HasBounty ? new Color(0.6f, 0.05f, 0.08f, 0.85f) : top.Crowned ? new Color(0.35f, 0.25f, 0.02f, 0.85f) : new Color(top.Glow.r * 0.25f, top.Glow.g * 0.25f, top.Glow.b * 0.3f, 0.78f);
                }
            }
            for (int i = used; i < _tags.Count; i++) if (_tags[i].root.gameObject.activeSelf) _tags[i].root.gameObject.SetActive(false);
        }

        Tag NewTag()
        {
            var bg = UiFactory.Panel("Tag", _tagsRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(120, 40), new Color(0f, 0f, 0f, 0.7f));
            var t = UiFactory.Label("Text", bg.transform, "", 27, TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 40));
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return new Tag { root = bg.rectTransform, bg = bg, text = t };
        }

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

        /// <summary>Annonce moyenne au centre (série d'éliminations, prime, duel final…).</summary>
        public void Announce(string text, Color color)
        {
            if (_announceQueue.Count > 5) _announceQueue.Dequeue();
            _announceQueue.Enqueue((text, color));
        }

        public void SetTaps(int total) => _taps.text = total > 0 ? "TAPOTEMENTS " + total.ToString("N0") : "";

        /// <summary>Tableau central (classement du live) avec un titre et des lignes déjà mises en forme.</summary>
        public void ShowBoard(string title, string lines, float seconds)
        {
            _boardTitle.text = title;
            _boardText.text = lines;
            _boardUntil = Time.unscaledTime + seconds;
        }

        public void ShowLeaderboard(List<PlayerProfile> top)
        {
            _boardTitle.text = "CLASSEMENT";
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
            // L'écran a tourné (option ou rotation du téléphone) : l'interface se réorganise.
            bool portraitNow = Screen.height > Screen.width;
            if (portraitNow != _portrait) BuildLayout(portraitNow);
            else if (ApplySafeArea()) BuildLayout(_portrait);
            // Portrait : écran épuré comme les jeux de live verticaux (l'arène d'abord, le chat TikTok couvre le bas).
            _listRoot.gameObject.SetActive(disp.showPlayerList && !_portrait);
            _legendRoot.gameObject.SetActive(disp.showGiftLegend && !_portrait);
            _hintPanel.gameObject.SetActive(disp.showHint && !_portrait);
            _status.gameObject.SetActive(disp.showStatus && !_portrait);
            UpdateGiftPanel(dt);
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
                _joinCard.anchoredPosition = new Vector2(-700, _joinPos.y);
            }
            bool joinOn = t < _joinUntil;
            _joinGroup.alpha = Mathf.MoveTowards(_joinGroup.alpha, joinOn ? 1f : 0f, dt * 5f);
            _joinCard.anchoredPosition = Vector2.Lerp(_joinCard.anchoredPosition, _joinPos, 1f - Mathf.Exp(-dt * 12f));

            _boardGroup.alpha = Mathf.MoveTowards(_boardGroup.alpha, t < _boardUntil && !_winnerVisible ? 1f : 0f, dt * 4f);

            _winnerGroup.alpha = Mathf.MoveTowards(_winnerGroup.alpha, _winnerVisible ? 1f : 0f, dt * 3f);
            _winnerCard.localScale = Vector3.one * Mathf.Lerp(_winnerCard.localScale.x, _winnerVisible ? 1f : 0.8f, 1f - Mathf.Exp(-dt * 8f));

            if (t > _announceUntil && _announceQueue.Count > 0)
            {
                var a = _announceQueue.Dequeue();
                _announce.text = a.text;
                _announce.color = a.color;
                _announceUntil = t + (_announceQueue.Count > 1 ? 1.4f : 2.4f);
                _announce.rectTransform.localScale = Vector3.one * 1.4f;
            }
            _announceGroup.alpha = Mathf.MoveTowards(_announceGroup.alpha, t < _announceUntil ? 1f : 0f, dt * 4f);
            _announce.rectTransform.localScale = Vector3.one * Mathf.Lerp(_announce.rectTransform.localScale.x, 1f, 1f - Mathf.Exp(-dt * 9f));
            _timer.color = TimerWarning && Mathf.Repeat(t, 1f) < 0.5f ? new Color(1f, 0.35f, 0.3f) : Color.white;

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
