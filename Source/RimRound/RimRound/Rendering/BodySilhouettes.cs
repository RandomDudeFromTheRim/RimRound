using System.Collections.Concurrent;
using UnityEngine;
using Verse;

namespace RimRound.Rendering
{
    /// <summary>
    /// The opaque shape of body sprites, so things attached to a body (tails) can be
    /// placed against its real outline. Sprites aren't CPU-readable, so each one is
    /// blitted to a small render texture and read back on the main thread (Pump);
    /// the renderer, which may run off the main thread, only reads finished masks and
    /// asks for the rest.
    /// </summary>
    public static class BodySilhouettes
    {
        const int Res = 64;

        public class Silhouette
        {
            public bool[] mask;          // Res x Res, row 0 at the sprite's bottom
            public float centroid;       // area centroid height, canvas fraction from the centre

            /// <summary>World height of the body's centre of mass, from the canvas centre.</summary>
            public float CentroidZ(float canvasWorld) => centroid * canvasWorld;

            /// <summary>
            /// World distance from origin (world, from the canvas centre) to the last opaque
            /// point along dir (x right, y up), facing rot on a canvas this wide.
            /// </summary>
            public float Reach(Rot4 rot, Vector2 origin, Vector2 dir, float canvasWorld)
            {
                if (rot == Rot4.West)
                {
                    // west is the east sprite mirrored
                    dir.x = -dir.x;
                    origin.x = -origin.x;
                }
                float ox = origin.x / canvasWorld, oy = origin.y / canvasWorld;
                float last = 0f;
                for (int i = 1; i <= Res * 2; i++)
                {
                    float t = i / (float)(Res * 2) * 0.75f;
                    int x = Mathf.FloorToInt((0.5f + ox + dir.x * t) * Res);
                    int y = Mathf.FloorToInt((0.5f + oy + dir.y * t) * Res);
                    if (x < 0 || y < 0 || x >= Res || y >= Res)
                        break;
                    if (mask[y * Res + x])
                        last = t;
                }
                return last * canvasWorld;
            }
        }

        static readonly ConcurrentDictionary<string, Silhouette> masks = new ConcurrentDictionary<string, Silhouette>();
        static readonly ConcurrentDictionary<string, bool> requested = new ConcurrentDictionary<string, bool>();
        static readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();

        static string Suffix(Rot4 rot) => rot == Rot4.North ? "_north" : rot == Rot4.South ? "_south" : "_east";

        /// <summary>The sprite at this path, facing rot. False until it has been measured.</summary>
        public static bool TryGet(string path, Rot4 rot, out Silhouette silhouette)
        {
            silhouette = null;
            if (path.NullOrEmpty())
                return false;
            string key = path + Suffix(rot);
            if (!masks.TryGetValue(key, out silhouette))
            {
                if (requested.TryAdd(key, true))
                    pending.Enqueue(key);
                return false;
            }
            return silhouette != null;
        }

        /// <summary>Main thread: measure a few waiting sprites.</summary>
        public static void Pump()
        {
            for (int n = 0; n < 4 && pending.TryDequeue(out string key); n++)
                masks[key] = Measure(ContentFinder<Texture2D>.Get(key, reportFailure: false));
        }

        static Silhouette Measure(Texture2D tex)
        {
            if (tex == null)
                return null;
            RenderTexture rt = RenderTexture.GetTemporary(Res, Res, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D read = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read = new Texture2D(Res, Res, TextureFormat.ARGB32, false);
                read.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
                read.Apply();
                Color32[] px = read.GetPixels32();
                var mask = new bool[Res * Res];          // row 0 is the sprite's bottom
                long count = 0;
                double rows = 0;
                for (int i = 0; i < mask.Length; i++)
                {
                    if (!(mask[i] = px[i].a > 127))
                        continue;
                    count++;
                    rows += i / Res;
                }
                if (count < 8)
                    return null;
                return new Silhouette { mask = mask, centroid = (float)((rows / count + 0.5) / Res - 0.5) };
            }
            catch (System.Exception e)
            {
                Log.WarningOnce($"[RimRound] Couldn't measure a body sprite ({e.Message}); attachments keep their usual place.", 0x52524253);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null)
                    Object.Destroy(read);
            }
        }
    }
}
