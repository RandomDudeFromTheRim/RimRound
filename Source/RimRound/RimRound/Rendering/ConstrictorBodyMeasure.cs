using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimRound.Rendering
{
    /// <summary>
    /// Measures where a constricted pawn's torso actually is on its body sprite, the
    /// same way ConstrictorBodyFit's table was generated (area-centroid row, and the
    /// sprite's width along it), but from the texture the pawn is really drawn with -
    /// so ratkin and other races with their own, narrower sprites get a band that fits
    /// them instead of one sized for the default bodies. Textures aren't CPU-readable,
    /// so the sprite is blitted to a small render texture and read back, once per
    /// texture, on the main thread; the renderer (off the main thread) only reads the
    /// cached result and falls back to the table until it's there.
    /// </summary>
    public static class ConstrictorBodyMeasure
    {
        const int Res = 128;

        static readonly Dictionary<Texture, ConstrictorBodyFit.Fit?> byTexture = new Dictionary<Texture, ConstrictorBodyFit.Fit?>();
        static readonly ConcurrentDictionary<int, ConstrictorBodyFit.Fit> byPawn = new ConcurrentDictionary<int, ConstrictorBodyFit.Fit>();

        public static bool TryGet(Pawn p, out ConstrictorBodyFit.Fit fit) => byPawn.TryGetValue(p.thingIDNumber, out fit);

        /// <summary>Main thread only: make sure this pawn's current body sprite is measured.</summary>
        public static void Update(Pawn p)
        {
            Texture tex = p.Drawer?.renderer?.BodyGraphic?.MatSouth?.mainTexture;
            if (tex == null)
                return;
            if (!byTexture.TryGetValue(tex, out ConstrictorBodyFit.Fit? fit))
            {
                fit = Measure(tex);
                byTexture[tex] = fit;
            }
            if (fit.HasValue)
                byPawn[p.thingIDNumber] = fit.Value;
            else
                byPawn.TryRemove(p.thingIDNumber, out _);
        }

        public static void Forget(Pawn p) => byPawn.TryRemove(p.thingIDNumber, out _);

        static ConstrictorBodyFit.Fit? Measure(Texture tex)
        {
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

                // rows run bottom-up here (y = 0 is the sprite's bottom edge)
                var rowCount = new int[Res];
                long total = 0;
                double weighted = 0;
                for (int y = 0; y < Res; y++)
                {
                    int n = 0;
                    for (int x = 0; x < Res; x++)
                        if (px[y * Res + x].a > 127)
                            n++;
                    rowCount[y] = n;
                    total += n;
                    weighted += (double)n * y;
                }
                if (total < Res)
                    return null;
                double cy = weighted / total;
                int row = Mathf.Clamp(Mathf.RoundToInt((float)cy), 0, Res - 1);
                int left = -1, right = -1;
                for (int x = 0; x < Res; x++)
                {
                    if (px[row * Res + x].a > 127)
                    {
                        if (left < 0)
                            left = x;
                        right = x;
                    }
                }
                if (left < 0)
                    return null;
                float z = (float)(cy + 0.5) / Res - 0.5f;
                float width = (right - left + 1) / (float)Res;
                return new ConstrictorBodyFit.Fit(z, width);
            }
            catch (System.Exception e)
            {
                Log.WarningOnce($"[RimRound] Couldn't measure a body sprite for the constrictor ({e.Message}); using the default fit.", 0x52524346);
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
