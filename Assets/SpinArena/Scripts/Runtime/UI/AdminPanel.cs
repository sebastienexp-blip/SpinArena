using SpinArena.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpinArena
{
    /// <summary>
    /// Panneau d'administration sur le téléphone : 3 tapotements rapides dans le coin en haut à gauche pour l'ouvrir.
    /// Démarrer / forcer la phase, pause, choix du connecteur, nombre de joueurs, durée, événements, statistiques.
    /// (Une régie à distance, séparée de l'image du live, est prévue avec le pont Termux.)
    /// </summary>
    public sealed class AdminPanel : MonoBehaviour
    {
        GameManager _gm;
        GameObject _panel;
        Text _stats, _maxText, _durText;
        int _taps;
        float _firstTap;

        public void Init(GameManager gm)
        {
            _gm = gm;
            var root = gm.Hud.Canvas.transform;
            // Zone invisible de déclenchement.
            var hot = UiFactory.Rect("AdminHotspot", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(140, 140));
            var img = hot.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = true;
            hot.gameObject.AddComponent<AdminHotspot>().Owner = this;

            var p = UiFactory.Panel("AdminPanel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 700), new Color(0.02f, 0.03f, 0.08f, 0.97f));
            p.raycastTarget = true;
            _panel = p.gameObject;
            var t = p.transform;
            Vector2 c = new Vector2(0.5f, 1f);
            UiFactory.Label("Title", t, "ADMINISTRATION", 40, TextAnchor.MiddleCenter, UiFactory.Gold, c, c, new Vector2(0, -16), new Vector2(900, 56));
            UiFactory.Button("Next", t, "Phase suivante", c, new Vector2(-250, -120), new Vector2(300, 74), () => gm.Battle.Round.ForceNextPhase());
            UiFactory.Button("Pause", t, "Pause / Reprise", c, new Vector2(80, -120), new Vector2(300, 74), () => gm.SetPaused(!gm.Battle.Round.Paused));
            UiFactory.Button("Close", t, "Fermer", c, new Vector2(390, -120), new Vector2(180, 74), () => _panel.SetActive(false));
            UiFactory.Button("Bridge", t, "Live TikTok (pont)", c, new Vector2(-250, -215), new Vector2(300, 74), () => gm.SetConnector("bridge"));
            UiFactory.Button("Demo", t, "Mode démo", c, new Vector2(80, -215), new Vector2(300, 74), () => gm.SetConnector("demo"));
            UiFactory.Button("Meteor", t, "Météores (test)", c, new Vector2(390, -215), new Vector2(180, 74), () => gm.Abilities.MeteorStorm(1));

            _maxText = UiFactory.Label("Max", t, "", 30, TextAnchor.MiddleCenter, Color.white, c, c, new Vector2(-170, -310), new Vector2(380, 60));
            UiFactory.Button("MaxMinus", t, "−5", c, new Vector2(-420, -310), new Vector2(110, 70), () => ChangeMax(-5));
            UiFactory.Button("MaxPlus", t, "+5", c, new Vector2(80, -310), new Vector2(110, 70), () => ChangeMax(5));
            _durText = UiFactory.Label("Dur", t, "", 30, TextAnchor.MiddleCenter, Color.white, c, c, new Vector2(-170, -395), new Vector2(380, 60));
            UiFactory.Button("DurMinus", t, "−30 s", c, new Vector2(-420, -395), new Vector2(110, 70), () => ChangeDuration(-30));
            UiFactory.Button("DurPlus", t, "+30 s", c, new Vector2(80, -395), new Vector2(110, 70), () => ChangeDuration(30));

            _stats = UiFactory.Label("Stats", t, "", 24, TextAnchor.UpperLeft, new Color(0.8f, 0.9f, 1f), c, c, new Vector2(0, -450), new Vector2(920, 230), false);
            _panel.SetActive(false);
            Refresh();
        }

        void ChangeMax(int d)
        {
            var r = _gm.Config.round;
            r.maxParticipants = Mathf.Clamp(r.maxParticipants + d, 5, 100);
            _gm.Config.SaveOverride();
            Refresh();
        }

        void ChangeDuration(int d)
        {
            var r = _gm.Config.round;
            r.maxFightSeconds = Mathf.Clamp(r.maxFightSeconds + d, 60, 600);
            _gm.Config.SaveOverride();
            Refresh();
        }

        void Refresh()
        {
            _maxText.text = "Joueurs max : " + _gm.Config.round.maxParticipants;
            _durText.text = "Durée du combat : " + Mathf.RoundToInt(_gm.Config.round.maxFightSeconds) + " s";
        }

        internal void Tap()
        {
            float t = Time.unscaledTime;
            if (t - _firstTap > 1.5f) { _taps = 0; _firstTap = t; }
            if (++_taps >= 3) { _taps = 0; _panel.SetActive(!_panel.activeSelf); Refresh(); }
        }

        void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;
            var g = _gm.Gifts.Processor;
            _stats.text =
                "Connecteur : " + _gm.Live.Name + " — " + _gm.Live.StatusText + "\n" +
                "Images/s : " + Mathf.RoundToInt(_gm.Fps) + "   ·   Joueurs enregistrés : " + _gm.Players.Registry.Count + "\n" +
                "Commandes acceptées : " + _gm.Commands.Accepted + "   ·   refusées (anti-abus) : " + _gm.Commands.Rejected + "\n" +
                "Dons reçus : " + g.Received + "   ·   exécutés : " + _gm.Gifts.Executed + "   ·   en attente : " + g.PendingCount + "   ·   doublons : " + g.Duplicates + "\n" +
                "Likes : " + _gm.Likes + "   ·   Photos de profil : " + AvatarCache.Instance.Downloaded + " (échecs " + AvatarCache.Instance.Failed + ")" +
                (_gm.Players.LastSaveError != null ? "\n<color=#FF8A80>Sauvegarde : " + _gm.Players.LastSaveError + "</color>" : "");
        }
    }

    /// <summary>Zone tactile qui transmet les tapotements au panneau d'administration.</summary>
    public sealed class AdminHotspot : MonoBehaviour, IPointerClickHandler
    {
        public AdminPanel Owner;
        public void OnPointerClick(PointerEventData eventData) => Owner?.Tap();
    }
}
