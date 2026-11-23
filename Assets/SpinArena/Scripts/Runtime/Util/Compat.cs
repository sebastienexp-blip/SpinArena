using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
using PhysCombine = UnityEngine.PhysicsMaterialCombine;
#else
using PhysMat = UnityEngine.PhysicMaterial;
using PhysCombine = UnityEngine.PhysicMaterialCombine;
#endif

namespace SpinArena.Util
{
    /// <summary>Petites aides de compatibilité entre versions de Unity et conversions.</summary>
    public static class Compat
    {
        public static Vector3 GetVelocity(this Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(this Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static void SetLinearDamping(this Rigidbody rb, float d)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = d;
#else
            rb.drag = d;
#endif
        }

        /// <summary>Applique un matériau physique (friction, rebond) à un collider.</summary>
        public static void SetPhysics(this Collider c, string name, float friction, float bounce, bool minFriction, bool maxBounce)
        {
            c.sharedMaterial = new PhysMat(name)
            {
                dynamicFriction = friction,
                staticFriction = friction,
                bounciness = bounce,
                frictionCombine = minFriction ? PhysCombine.Minimum : PhysCombine.Average,
                bounceCombine = maxBounce ? PhysCombine.Maximum : PhysCombine.Minimum
            };
        }

        public static void SetAngularDamping(this Rigidbody rb, float d)
        {
#if UNITY_6000_0_OR_NEWER
            rb.angularDamping = d;
#else
            rb.angularDrag = d;
#endif
        }

        public static Color Hex(string hex, Color fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }

        public static Color Hex(string hex) => Hex(hex, Color.white);

        public static Vector3 Flat(this Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static Font UiFont
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Roboto", 32);
                }
                return _font;
            }
        }
        static Font _font;
    }
}
