using System.Collections.Concurrent;
using UnityEngine;
using Verse;

namespace RimRound.Rendering
{
    /// <summary>
    /// Holds the subdivided body meshes. Milestone 1: the vertices are exactly the vanilla quad,
    /// merely subdivided, so the sprite must look pixel-identical to the unmodified game. If it
    /// looks shifted, flipped or tiled, this is where the mistake is.
    ///
    /// A mesh must never be built inside the draw path: AppendDrawRequests runs in
    /// PawnRenderTree.ParallelPreDraw, a parallel job. Everything here is built on the main thread
    /// (from Pawn.Tick via EnsureBuilt) and afterwards only read.
    ///
    /// Geometry follows MeshMakerPlanes.NewPlaneMesh exactly:
    ///   flat in XZ at y = 0, uv (0,0) at (-x,-z), (1,1) at (+x,+z), winding a-d-c / a-c-b.
    /// </summary>
    public static class SquishMeshCache
    {
        public const int Nx = 8;
        public const int Ny = 12;

        static readonly ConcurrentDictionary<Vector2, Mesh> meshes = new ConcurrentDictionary<Vector2, Mesh>();

        public static bool MeshEnabled = true;

        static Vector2 Key(Vector2 size) =>
            new Vector2(Mathf.Round(size.x * 100f) / 100f, Mathf.Round(size.y * 100f) / 100f);

        /// <summary>Main thread only. Safe to call repeatedly.</summary>
        public static Mesh EnsureBuilt(Vector2 size)
        {
            Vector2 key = Key(size);
            if (meshes.TryGetValue(key, out Mesh existing))
                return existing;
            Mesh built = Build(key);
            return meshes.GetOrAdd(key, built);
        }

        /// <summary>Read-only lookup, safe from the parallel draw path.</summary>
        public static bool TryGet(Vector2 size, out Mesh mesh) => meshes.TryGetValue(Key(size), out mesh);

        public static int Count => meshes.Count;

        static Mesh Build(Vector2 size)
        {
            var verts = new Vector3[Nx * Ny];
            var uvs = new Vector2[Nx * Ny];
            for (int j = 0; j < Ny; j++)
            {
                float v = j / (float)(Ny - 1);
                for (int i = 0; i < Nx; i++)
                {
                    float u = i / (float)(Nx - 1);
                    verts[j * Nx + i] = new Vector3(-0.5f * size.x + u * size.x, 0f,
                                                    -0.5f * size.y + v * size.y);
                    uvs[j * Nx + i] = new Vector2(u, v);
                }
            }

            var tris = new int[(Nx - 1) * (Ny - 1) * 6];
            int t = 0;
            for (int j = 0; j < Ny - 1; j++)
            {
                for (int i = 0; i < Nx - 1; i++)
                {
                    int a = j * Nx + i;            // (-x, -z)
                    int b = j * Nx + i + 1;        // (+x, -z)
                    int c = (j + 1) * Nx + i + 1;  // (+x, +z)
                    int d = (j + 1) * Nx + i;      // (-x, +z)
                    tris[t++] = a; tris[t++] = d; tris[t++] = c;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                }
            }

            var mesh = new Mesh();
            mesh.name = $"RR_SquishGrid_{size.x:0.00}x{size.y:0.00}";
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void Clear()
        {
            foreach (Mesh m in meshes.Values)
            {
                if (m != null)
                    Object.Destroy(m);
            }
            meshes.Clear();
        }
    }
}
