using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// What a structural break looks like (docs/DAMAGE_MODEL.md, step 8).
    ///
    /// A part that comes off leaves torn structure on both sides of the break, not a clean cut:
    /// - the skin, ripped back in jagged petals that curl outward, bare aluminium outside and sooty inside;
    /// - the internal structure sticking out of it: on a wing the front and rear spar webs (broken off at different
    ///   lengths, top and bottom) and the stringers along the skin; on a fuselage or pod the stringers, two longerons and
    ///   part of the frame nearest the break;
    /// - the first bulkhead or rib, a little way in, closing the hole;
    /// - a burst of skin fragments and a puff of smoke at the break, left behind in the airflow;
    /// - the piece that came off folding away about its root under the load it was carrying (lift on a wing folds it up,
    ///   drag on a pod tears it back).
    /// All of it is built from the part's own geometry and a seed from its name, so every client sees the same breaks.
    /// The torn structure rides on the part it belongs to from then on.
    /// </summary>
    internal static class BreakStubs
    {
        private static Material skinMat, sootMat, smokeMat;
        private static Mesh shard;

        internal static void Make(AeroPart part, AeroPart parent, Vector3 rootW, Vector3 toParentW, List<Vector3> skinW, List<Vector3> parentW)
        {
            if (!parent || skinW == null || skinW.Count < 8) return;
            var n = toParentW.normalized;
            // Section: where the two parts' skins meet (the vertices of the departing part that lie on the parent's: the
            // seam the parts were cut along). Where there is no shared seam (a pod's pylon on the wing's underside), the
            // departing part's face nearest the parent: its skin within 0.3 m of its furthest point towards the parent.
            var near = Seam(skinW, parentW);
            if (near.Count >= 6)
            {
                // The seam's own plane (a cut across a swept wing is not square to the line between the parts).
                var sn = PlaneNormal(near);
                if (sn != Vector3.zero) n = Vector3.Dot(sn, n) < 0f ? -sn : sn;
            }
            else
            {
                // Towards the parent: from the part's centre to the nearest point of the parent's skin (a pod: up its pylon).
                if (parentW != null && parentW.Count > 0)
                {
                    var c = Vector3.zero; foreach (var p in skinW) c += p; c /= skinW.Count;
                    var nearest = parentW.OrderBy(p => (p - c).sqrMagnitude).First();
                    if ((nearest - c).sqrMagnitude > 0.01f) n = (nearest - c).normalized;
                }
                float top = skinW.Max(v => Vector3.Dot(v, n));
                near = skinW.Where(v => Vector3.Dot(v, n) > top - 0.3f).ToList();
            }
            if (near.Count < 3) return;
            // The break lies in the plane through the section, across the part.
            var mid = Vector3.zero; foreach (var p in near) mid += p; mid /= near.Count;
            rootW = mid;
            var u = Vector3.Cross(n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var w = Vector3.Cross(n, u);
            var hull = Hull(near.Select(v => new Vector2(Vector3.Dot(v - rootW, u), Vector3.Dot(v - rootW, w))).ToList());
            if (hull.Count < 3) return;
            var sec = new Section(hull, rootW, u, w);
            Materials(parent);

            // The parent keeps the longer torn structure; the piece that came off, a shorter one of its own.
            int seed = part.name.GetHashCode() ^ 0x52b52;
            // The structure inside both parts (tools/blender_structure.py) comes into view through the break; where a part
            // has none, a bulkhead closes the hole instead.
            bool parentInside = ShowStructure(parent), partInside = ShowStructure(part);
            Build(part.name + "_stub", parent.transform, sec, -n, new System.Random(seed), 1f, !parentInside);
            Build(part.name + "_rootstub", part.transform, sec, n, new System.Random(seed ^ 0x7a3), 0.65f, !partInside);

            var vel = parent.rb ? parent.rb.GetPointVelocity(rootW) : Vector3.zero;
            Burst(rootW, vel, sec.size, new System.Random(seed ^ 0x1d), parent);
            Fold(part, rootW);
            Plugin.Log.LogInfo($"B-52J {part.name}: torn structure on {parent.name} and the piece, {hull.Count}-point section " +
                               $"{sec.major:F1} x {sec.minor:F1} m ({(sec.wing ? "wing box" : "fuselage frame")})");
        }

        // ------------------------------------------------------------------ the break's section

        // Normal of the plane that best fits the points: across the two directions they spread furthest in.
        private static Vector3 PlaneNormal(List<Vector3> pts)
        {
            var c = Vector3.zero; foreach (var p in pts) c += p; c /= pts.Count;
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts) { var d = p - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
            Vector3 Mul(Vector3 v) => new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
            Vector3 Power(Vector3 v, Vector3 not)
            {
                for (int i = 0; i < 40; i++) { v = Mul(v); v -= Vector3.Dot(v, not) * not; if (v.sqrMagnitude < 1e-12f) return Vector3.zero; v.Normalize(); }
                return v;
            }
            var e1 = Power(new Vector3(0.6f, 0.5f, 0.62f), Vector3.zero);
            if (e1 == Vector3.zero) return Vector3.zero;
            var seed = Vector3.Cross(e1, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(e1, Vector3.up) : Vector3.Cross(e1, Vector3.right);
            var e2 = Power(seed.normalized, e1);
            return e2 == Vector3.zero ? Vector3.zero : Vector3.Cross(e1, e2).normalized;
        }

        // The departing part's vertices within 8 cm of one of the parent's (a 0.1 m grid of the parent's vertices).
        private static List<Vector3> Seam(List<Vector3> skin, List<Vector3> parentSkin)
        {
            var seam = new List<Vector3>();
            if (parentSkin == null || parentSkin.Count == 0) return seam;
            const float cell = 0.1f, tol = 0.08f;
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
            var grid = new Dictionary<Vector3Int, List<Vector3>>();
            foreach (var p in parentSkin)
            {
                var k = Key(p);
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Vector3>();
                l.Add(p);
            }
            foreach (var v in skin)
            {
                var k = Key(v);
                bool hit = false;
                for (int x = -1; x <= 1 && !hit; x++)
                    for (int y = -1; y <= 1 && !hit; y++)
                        for (int z = -1; z <= 1 && !hit; z++)
                            if (grid.TryGetValue(k + new Vector3Int(x, y, z), out var l))
                                foreach (var p in l) if ((p - v).sqrMagnitude < tol * tol) { hit = true; break; }
                if (hit) seam.Add(v);
            }
            return seam;
        }

        private sealed class Section
        {
            public readonly List<Vector2> hull;
            public readonly Vector2 c;            // centroid, in the root plane
            public readonly Vector2 ax, ay;       // major (chord) and minor (thickness) axes
            public readonly float major, minor, lo, size;
            public readonly bool wing;
            readonly Vector3 root, u, w;

            public Section(List<Vector2> hull, Vector3 root, Vector3 u, Vector3 w)
            {
                this.hull = hull; this.root = root; this.u = u; this.w = w;
                c = new Vector2(hull.Average(p => p.x), hull.Average(p => p.y));
                float sxx = 0, syy = 0, sxy = 0;
                foreach (var p in hull) { var d = p - c; sxx += d.x * d.x; syy += d.y * d.y; sxy += d.x * d.y; }
                float ang = 0.5f * Mathf.Atan2(2f * sxy, sxx - syy);
                ax = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)); ay = new Vector2(-ax.y, ax.x);
                float hi = hull.Max(p => Vector2.Dot(p - c, ax)); lo = hull.Min(p => Vector2.Dot(p - c, ax));
                major = hi - lo;
                minor = hull.Max(p => Vector2.Dot(p - c, ay)) - hull.Min(p => Vector2.Dot(p - c, ay));
                wing = major > 2.5f * minor;
                size = Mathf.Clamp(major / 4f, 0.35f, 1f);
            }

            public Vector3 W(Vector2 p) => root + u * p.x + w * p.y;
            public Vector3 Dir(Vector2 d) => (u * d.x + w * d.y).normalized;
            public Vector2 Edge(Vector2 from, Vector2 dir) => from + dir * RayHull(hull, from, dir);
        }

        // ------------------------------------------------------------------ torn structure

        // Switches on the part's internal structure ("<part>_struct", built switched off). False if it has none.
        private static bool ShowStructure(AeroPart p)
        {
            var t = p ? p.transform.Find(p.name + "_struct") : null;
            var r = t ? t.GetComponent<MeshRenderer>() : null;
            if (!r) return false;
            r.enabled = true;
            return true;
        }

        private static void Build(string name, Transform holder, Section s, Vector3 outW, System.Random rng, float len, bool cap)
        {
            float R() => (float)rng.NextDouble();
            float k = s.size * len;
            var v = new List<Vector3>(); var skin = new List<int>(); var soot = new List<int>();

            // Skin: 48 points round the section, each torn back to its own length (alternately long and short, so the
            // edge is jagged), in three rows that curl outward.
            const int N = 48;
            var rows = new Vector3[N, 3];
            var radial = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                float a = (i + 0.3f * R()) * Mathf.PI * 2f / N;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var p2 = s.Edge(s.c, dir);
                radial[i] = s.Dir(dir);
                float L = (0.06f + 0.5f * Mathf.Pow(R(), 1.5f) * (i % 2 == 0 ? 1f : 0.45f)) * k;
                if (R() < 0.12f) L *= 2.2f;                               // the odd long petal
                float curl = 0.3f * R();
                var b = s.W(p2);
                rows[i, 0] = b;
                rows[i, 1] = b + outW * 0.55f * L + radial[i] * curl * 0.15f * L;
                rows[i, 2] = b + outW * L + radial[i] * curl * 0.6f * L;
            }
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;
                for (int r = 0; r < 2; r++)
                    TwoSided(v, skin, soot, rows[i, r], rows[j, r], rows[j, r + 1], rows[i, r + 1], (radial[i] + radial[j]).normalized);
            }

            // The first bulkhead or rib, a little way in: closes the hole.
            var back = -outW * 0.3f * k;
            var ctr = s.W(s.c) + back;
            for (int i = 0; i < N && cap; i++)
            {
                int j = (i + 1) % N;
                Tri(v, soot, ctr, rows[i, 0] + back, rows[j, 0] + back);
                Tri(v, soot, ctr, rows[j, 0] + back, rows[i, 0] + back);
            }

            if (s.wing)
            {
                // Front and rear spar webs at 22% and 62% chord, each snapped off at its own length top and bottom.
                foreach (var f in new[] { 0.22f, 0.62f })
                {
                    var at = s.c + s.ax * (s.lo + f * s.major);
                    var top = s.Edge(at, s.ay); var bot = s.Edge(at, -s.ay);
                    var t3 = s.W(top) - s.Dir(s.ay) * 0.02f; var b3 = s.W(bot) + s.Dir(s.ay) * 0.02f;
                    var across = s.Dir(s.ax) * 0.035f;
                    float lt = (0.15f + 0.4f * R()) * k, lb = (0.15f + 0.4f * R()) * k;
                    var tipT = t3 + outW * lt + across * (R() - 0.5f) * 4f;
                    var tipB = b3 + outW * lb + across * (R() - 0.5f) * 4f;
                    Box(v, skin, new[] { t3 - across, t3 + across, b3 + across, b3 - across, tipT - across, tipT + across, tipB + across, tipB - across });
                    // A ragged bite out of the web's end, halfway up.
                    var mid = (tipT + tipB) * 0.5f - outW * (0.25f * R() * Mathf.Min(lt, lb));
                    Box(v, skin, new[] { (t3 + b3) * 0.5f - across, (t3 + b3) * 0.5f + across, b3 + across, b3 - across, mid - across, mid + across, tipB + across, tipB - across });
                }
            }
            else
            {
                // Two longerons, thicker than the stringers, and what's left of the frame nearest the break.
                for (int l = 0; l < 2; l++)
                {
                    int i = (int)(R() * N) % N;
                    Rod(v, skin, rows[i, 0] - radial[i] * 0.06f, outW * (0.2f + 0.4f * R()) * k + radial[i] * 0.06f * R(), radial[i], 0.04f);
                }
                int start = (int)(R() * N), arc = (int)(N * (0.55f + 0.3f * R()));
                var frameOut = outW * 0.18f * k;
                for (int a = 0; a < arc; a++)
                {
                    int i = (start + a) % N, j = (start + a + 1) % N;
                    var pi = rows[i, 0] + frameOut; var pj = rows[j, 0] + frameOut;
                    var di = pi - radial[i] * 0.12f; var dj = pj - radial[j] * 0.12f;
                    var th = outW * 0.03f;
                    Box(v, skin, new[] { pi - th, pj - th, dj - th, di - th, pi + th, pj + th, dj + th, di + th });
                }
            }

            // Stringers along the skin, each snapped at its own length and bent a little.
            int count = s.wing ? 12 : 16;
            float off = R();
            for (int m = 0; m < count; m++)
            {
                int i = (int)((m + off) * N / count) % N;
                var tangent = (rows[(i + 1) % N, 0] - rows[i, 0]).normalized;
                var span = outW * (0.08f + 0.35f * R()) * k + radial[i] * 0.06f * R() * k + tangent * 0.08f * (R() - 0.5f) * k;
                Rod(v, skin, rows[i, 0] - radial[i] * 0.03f, span, radial[i], 0.018f);
            }

            var go = new GameObject(name);
            go.transform.SetParent(holder, false);
            var t = go.transform;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v.Select(p => t.InverseTransformPoint(p)).ToList());
            mesh.subMeshCount = 2;
            mesh.SetTriangles(skin, 0); mesh.SetTriangles(soot, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { skinMat, sootMat };
            go.layer = holder.gameObject.layer;
        }

        // A quad seen from both sides: bare metal facing `outward`, soot on the other face.
        private static void TwoSided(List<Vector3> v, List<int> outer, List<int> inner, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            if (flip) { Tri(v, outer, a, c, b); Tri(v, outer, a, d, c); Tri(v, inner, a, b, c); Tri(v, inner, a, c, d); }
            else { Tri(v, outer, a, b, c); Tri(v, outer, a, c, d); Tri(v, inner, a, c, b); Tri(v, inner, a, d, c); }
        }

        private static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        // Eight corners (base ring of four, then the end's four in the same order); flat faces, each face both ways so
        // the winding never matters.
        private static void Box(List<Vector3> v, List<int> t, Vector3[] q)
        {
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            foreach (var f in faces)
            {
                Tri(v, t, q[f[0]], q[f[1]], q[f[2]]); Tri(v, t, q[f[0]], q[f[2]], q[f[3]]);
                Tri(v, t, q[f[0]], q[f[2]], q[f[1]]); Tri(v, t, q[f[0]], q[f[3]], q[f[2]]);
            }
        }

        // A square-section rod from `from` along `span`, `side` its half-width; `up` sets which way its faces lie.
        private static void Rod(List<Vector3> v, List<int> t, Vector3 from, Vector3 span, Vector3 up, float side)
        {
            var a1 = Vector3.Cross(span, up).normalized * side;
            var a2 = Vector3.Cross(span, a1).normalized * side;
            var e = from + span;
            Box(v, t, new[] { from - a1 - a2, from + a1 - a2, from + a1 + a2, from - a1 + a2, e - a1 - a2, e + a1 - a2, e + a1 + a2, e - a1 + a2 });
        }

        // ------------------------------------------------------------------ fragments and smoke

        private static void Burst(Vector3 at, Vector3 vel, float k, System.Random rng, AeroPart parent)
        {
            float R() => (float)rng.NextDouble();
            Vector3 Rand() => new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1);

            // Skin fragments: thin bent shards of metal, flung out of the break and falling away behind.
            var go = new GameObject("B52_break_fragments");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f; main.maxParticles = 80; main.startSize3D = true; main.startRotation3D = true;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 0.8f; drag.multiplyDragByParticleSize = false; drag.limit = 1e4f;
            var spin = ps.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-8f, 8f); spin.y = new ParticleSystem.MinMaxCurve(-8f, 8f); spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Mesh; pr.mesh = Shard(); pr.sharedMaterial = skinMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            int count = Mathf.RoundToInt(55 * k);
            for (int i = 0; i < count; i++)
            {
                float s = 0.08f + 0.45f * R() * R();
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Rand() * 0.6f * k,
                    velocity = vel + Rand().normalized * (4f + 18f * R()),
                    startSize3D = new Vector3(s, s * (0.5f + R()), s),
                    rotation3D = Rand() * 180f,
                    startLifetime = 2.5f + 3f * R(),
                    startColor = i % 3 == 0 ? new Color(0.15f, 0.14f, 0.13f) : Color.white,
                }, 1);
            }
            ps.Play();
            Object.Destroy(go, 7f);
            go.layer = parent.gameObject.layer;

            // Smoke: a grey puff torn from the break that stays where it was made as the aircraft flies on.
            if (!smokeMat) return;
            var sg = new GameObject("B52_break_smoke");
            sg.transform.position = at;
            var sp = sg.AddComponent<ParticleSystem>();
            sp.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = sp.main;
            sm.playOnAwake = false; sm.loop = false; sm.duration = 1f; sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.maxParticles = 30; sm.gravityModifier = -0.05f;
            var sem = sp.emission; sem.enabled = false;
            var ssh = sp.shape; ssh.enabled = false;
            var sdrag = sp.limitVelocityOverLifetime; sdrag.enabled = true; sdrag.drag = 2.5f; sdrag.multiplyDragByParticleSize = false; sdrag.limit = 1e4f;
            var size = sp.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1f));
            var col = sp.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.35f, 0.34f, 0.33f), 0f), new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f) },
                      new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.45f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sr = sg.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Billboard; sr.sharedMaterial = smokeMat;
            for (int i = 0; i < 14; i++)
                sp.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Rand() * 1.2f * k,
                    velocity = vel * 0.35f + Rand() * 4f,
                    startSize = (5f + 7f * R()) * k,
                    rotation = 360f * R(),
                    startLifetime = 3f + 3f * R(),
                    startColor = Color.white,
                }, 1);
            sp.Play();
            Object.Destroy(sg, 8f);
            sg.layer = parent.gameObject.layer;
        }

        // A fragment of skin: a small bent plate.
        private static Mesh Shard()
        {
            if (shard) return shard;
            var v = new List<Vector3>(); var t = new List<int>();
            var p = new[] { new Vector3(-0.5f, 0f, -0.4f), new Vector3(0.45f, 0.05f, -0.5f), new Vector3(0.5f, 0.15f, 0.35f), new Vector3(0.05f, -0.1f, 0.5f), new Vector3(-0.45f, 0.1f, 0.2f) };
            var c = new Vector3(0f, 0.2f, 0f);                            // bent up in the middle
            for (int i = 0; i < p.Length; i++) { Tri(v, t, c, p[i], p[(i + 1) % p.Length]); Tri(v, t, c, p[(i + 1) % p.Length], p[i]); }
            shard = new Mesh { name = "B52_shard" };
            shard.SetVertices(v); shard.SetTriangles(t, 0); shard.RecalculateNormals(); shard.RecalculateBounds();
            return shard;
        }

        // ------------------------------------------------------------------ the piece folding away

        // The piece that came off swings away about its root under the load it carried: its aero force (lift on a wing,
        // drag on a pod) and its weight, at the arm from the root to its centre of mass. A tip speed of 6 m/s at most,
        // on top of the motion the game gives it.
        private static void Fold(AeroPart part, Vector3 rootW)
        {
            var body = part.rb;
            if (!body || body.isKinematic) return;
            var arm = body.worldCenterOfMass - rootW;
            if (arm.sqrMagnitude < 0.25f) return;
            var load = AeroCentres.Force(part) + Physics.gravity * body.mass;
            if (load.sqrMagnitude < 1f) return;
            var omega = Vector3.Cross(arm, load.normalized) / arm.sqrMagnitude * 6f;
            body.angularVelocity += Vector3.ClampMagnitude(omega, 2.5f);
        }

        // ------------------------------------------------------------------ materials

        // Bare torn aluminium and soot: copies of a lit material from the aircraft with their textures cleared. The smoke
        // borrows the B-52's own tyre-dust material.
        private static void Materials(AeroPart parent)
        {
            if (skinMat && sootMat) return;
            var aircraft = parent.GetComponentInParent<Aircraft>();
            var src = aircraft.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m && m.shader && m.shader.name.Contains("Universal Render Pipeline/Lit"));
            if (src)
            {
                skinMat = Plain(src, "B52_torn_skin", new Color(0.62f, 0.63f, 0.65f), 0.35f, 0.35f);   // bare aluminium
                sootMat = Plain(src, "B52_torn_soot", new Color(0.07f, 0.065f, 0.06f), 0.3f, 0.15f);
            }
            if (!smokeMat)
            {
                var dust = aircraft.GetComponentsInChildren<ParticleSystemRenderer>(true).FirstOrDefault(r => r.name.StartsWith("wheel_") && r.sharedMaterial);
                if (dust) smokeMat = dust.sharedMaterial;
            }
        }

        private static Material Plain(Material src, string name, Color c, float metal, float smooth)
        {
            var m = new Material(src) { name = name };
            foreach (var tex in new[] { "_BaseMap", "_MainTex", "_EmissionMap", "_MetallicGlossMap", "_BumpMap", "_OcclusionMap" }) if (m.HasProperty(tex)) m.SetTexture(tex, null);
            m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
            m.DisableKeyword("_EMISSION"); m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_OCCLUSIONMAP");
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 2f);
            return m;
        }

        // ------------------------------------------------------------------ geometry

        private static List<Vector2> Hull(List<Vector2> p)
        {
            p = p.Distinct().OrderBy(a => a.x).ThenBy(a => a.y).ToList();
            if (p.Count < 3) return p;
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var h = new List<Vector2>();
            foreach (var pt in p) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], pt) <= 0) h.RemoveAt(h.Count - 1); h.Add(pt); }
            int lower = h.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--) { var pt = p[i]; while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], pt) <= 0) h.RemoveAt(h.Count - 1); h.Add(pt); }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        // Distance from c along dir to the hull's edge.
        private static float RayHull(List<Vector2> hull, Vector2 c, Vector2 dir)
        {
            float best = 0f;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 a = hull[i] - c, b = hull[(i + 1) % hull.Count] - c, e = b - a;
                float den = dir.x * e.y - dir.y * e.x;
                if (Mathf.Abs(den) < 1e-6f) continue;
                float t = (a.x * e.y - a.y * e.x) / den, s = (a.x * dir.y - a.y * dir.x) / den;
                if (t > 0 && s >= 0 && s <= 1) best = Mathf.Max(best, t);
            }
            return best;
        }
    }
}
