using System;

namespace SpinArena.Core.Live
{
    public enum LiveEventType { Chat, Gift, Like, Member, Follow, Share, Status }

    /// <summary>Spectateur tel que fourni par le connecteur. Aucune donnée n'est inventée : un champ vide reste vide.</summary>
    [Serializable]
    public sealed class LiveUser
    {
        public string userId;      // identifiant numérique stable
        public string uniqueId;    // @pseudo
        public string nickname;    // nom affiché
        public string avatarUrl;   // photo de profil (peut être vide)

        public string DisplayName => !string.IsNullOrEmpty(nickname) ? nickname : !string.IsNullOrEmpty(uniqueId) ? uniqueId : "Spectateur";
        /// <summary>Clé de joueur : l'identifiant numérique s'il existe, sinon le @pseudo.</summary>
        public string Key => !string.IsNullOrEmpty(userId) ? userId : uniqueId;
    }

    /// <summary>Événement normalisé, indépendant de la bibliothèque TikTok utilisée.</summary>
    [Serializable]
    public sealed class LiveEvent
    {
        public LiveEventType type;
        public string msgId;          // identifiant du message (anti-doublon) ; peut être vide
        public LiveUser user;
        public string comment;        // Chat
        public string giftId;         // Gift
        public string giftName;
        public int diamondCount;      // valeur unitaire du cadeau
        public int repeatCount;       // compteur de la série (cadeaux enchaînés)
        public bool repeatEnd;        // fin de la série
        public bool streakable;       // cadeau enchaînable (giftType 1)
        public string groupId;        // identifiant de série, si fourni
        public string giftImageUrl;
        public int likeCount;         // Like
        public string status;         // Status : connected / disconnected / error / demo…
        public double receivedAt;     // horloge locale (s)
    }

    /// <summary>
    /// Couche d'abstraction du live : le jeu ne connaît que cette interface. On peut remplacer le connecteur
    /// (pont Termux, connecteur de démonstration, futur accès officiel) sans toucher au reste du jeu.
    /// </summary>
    public interface ILiveConnector
    {
        string Name { get; }
        bool IsDemo { get; }
        bool IsConnected { get; }
        string StatusText { get; }
        event Action<LiveEvent> EventReceived;
        void Connect();
        void Disconnect();
        /// <summary>Appelé à chaque image sur le fil principal (distribution des événements reçus).</summary>
        void Pump(double now);
    }
}
