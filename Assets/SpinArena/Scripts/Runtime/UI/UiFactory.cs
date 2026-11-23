using SpinArena.Util;
using UnityEngine;
using UnityEngine.UI;

namespace SpinArena.UI
{
    /// <summary>Petites briques d'interface construites par code (pas de prefab à maintenir).</summary>
    public static class UiFactory
    {
        public static readonly Color PanelColor = new Color(0.03f, 0.05f, 0.12f, 0.72f);
        public static readonly Color Accent = new Color(0.30f, 0.85f, 1f);
        public static readonly Color Gold = new Color(1f, 0.8f, 0.25f);

        static Sprite _rounded;
        /// <summary>Sprite arrondi (9-slice) généré une fois pour les panneaux.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded != null) return _rounded;
                const int n = 64, rad = 18;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Rounded" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cx = Mathf.Clamp(x, rad, n - 1 - rad), cy = Mathf.Clamp(y, rad, n - 1 - rad);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    byte a = (byte)(Mathf.Clamp01(rad - d) * 255f);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
                t.SetPixels32(px);
                t.Apply();
                _rounded = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(rad, rad, rad, rad));
                return _rounded;
            }
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static Image Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Rect(name, parent, anchor, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, TextAnchor align, Color color, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, bool outline = true)
        {
            var rt = Rect(name, parent, anchor, anchor, pivot, pos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Compat.UiFont;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.85f);
                o.effectDistance = new Vector2(2f, -2f);
            }
            return t;
        }

        public static RawImage Picture(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, float size)
        {
            var rt = Rect(name, parent, anchor, anchor, pivot, pos, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Barre de progression (fond + remplissage horizontal).</summary>
        public static Image Bar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fill)
        {
            var bg = Panel(name, parent, anchor, pivot, pos, size, new Color(0f, 0f, 0f, 0.55f));
            var fg = Rect("Fill", bg.transform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            var img = fg.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.color = fill;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Règle la longueur d'une barre créée par <see cref="Bar"/> (0..1).</summary>
        public static void SetBar(Image fill, float value)
        {
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        public static Button Button(string name, Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, System.Action onClick)
        {
            var img = Panel(name, parent, anchor, new Vector2(0.5f, 0.5f), pos, size, new Color(0.12f, 0.2f, 0.4f, 0.95f));
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
            Label("Text", img.transform, text, 26, TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(10, 4));
            return b;
        }
    }
}
