using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The orbiting motes — the layer that states the sphere has a VOLUME rather than an
    /// outline.
    ///
    /// <para>Each mote runs a GREAT CIRCLE at its own random tilt, which is what produces the
    /// variety the illusion needs: an orbit whose axis points at the camera hugs the rim and
    /// never crosses the character, while one lying in the screen plane sweeps straight through
    /// the middle, passing in front of them and then behind. A ring of motes all sharing one
    /// plane is a halo, and a halo is exactly what this rig exists to stop being.</para>
    ///
    /// <para>Three quantities are read off the same depth value and they have to agree, or the
    /// motion reads as sliding rather than orbiting: the mote SHRINKS going away, DIMS going
    /// away, and SORTS behind the caster once past the halfway point.</para>
    /// </summary>
    internal sealed partial class ShieldSphereFX
    {
        /// <summary>How fast an impact-displaced mote is pulled back onto the shell.</summary>
        private const float MOTE_SPRING = 5.5f;

        private void BuildMotes()
        {
            for (int i = 0; i < MOTE_COUNT; i++)
            {
                Vector3 axis = RandomUnitVector();
                // Any unit vector perpendicular to the axis will do as the orbit's zero point;
                // cross against whichever cardinal the axis is least aligned with, so the
                // result is never a near-zero vector.
                Vector3 reference = Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 u = Vector3.Cross(axis, reference).normalized;
                Vector3 v = Vector3.Cross(axis, u).normalized;

                var mote = new Mote
                {
                    U = u,
                    V = v,
                    Speed = Range(0.55f, 1.5f) * (_rng.Next(2) == 0 ? 1f : -1f),
                    Phase = Range(0f, Mathf.PI * 2f),
                    Size = _config.Radius * Range(0.10f, 0.22f),
                    // Not all on the surface: a shell one mote thick reads as a wire cage.
                    Shell = Range(0.90f, 1.06f),
                };

                mote.Renderer = MakeSprite("Mote" + i, ShieldSprites.Mote, _root, FRONT_ORDER);
                mote.Root = mote.Renderer.transform;
                SetColor(mote.Renderer, _config.Palette.Core, 0f);
                _motes.Add(mote);
            }
        }

        private void UpdateMotes(float envelope, float moteRadiusFactor, float deltaTime,
            float breakTime)
        {
            for (int i = 0; i < _motes.Count; i++)
            {
                var mote = _motes[i];
                if (mote.Root == null) continue;

                mote.Push = Mathf.Lerp(mote.Push, 0f, Mathf.Clamp01(deltaTime * MOTE_SPRING));

                float theta = _age * mote.Speed + mote.Phase;
                Vector3 p = mote.U * Mathf.Cos(theta) + mote.V * Mathf.Sin(theta);

                float shell = (mote.Shell + mote.Push) * moteRadiusFactor + breakTime * 2.4f;
                mote.Root.localPosition = _config.BodyOffset +
                                          new Vector3(p.x, p.y, 0f) * (_config.Radius * shell);

                // Depth runs -1 (far side) to +1 (near side). Everything below reads off it.
                float near01 = p.z * 0.5f + 0.5f;

                float scale = mote.Size * Mathf.Lerp(0.55f, 1.30f, near01);
                mote.Root.localScale = new Vector3(scale, scale, 1f);

                bool inFront = p.z >= 0f;
                if (inFront != mote.InFront)
                {
                    mote.InFront = inFront;
                    mote.Renderer.sortingOrder = _baseOrder + (inFront ? FRONT_ORDER : BACK_ORDER);
                }

                float alpha = Mathf.Lerp(0.26f, 1f, near01) * envelope * (1f - breakTime);
                // A mote crossing the silhouette catches the rim light, same Fresnel logic as
                // the facets — it keeps the two layers looking like one surface.
                alpha *= 0.85f + 0.35f * (1f - Mathf.Abs(p.z));

                SetColor(mote.Renderer,
                    Color.Lerp(_config.Palette.Mid, _config.Palette.Core, near01), alpha);
            }
        }

        /// <summary>
        /// Kick the motes near a point of contact outward. They spring back on their own, so
        /// this is a single displacement rather than a state — the shell bulges where it was
        /// hit and recovers, which is the cheapest possible statement that it is under load.
        /// </summary>
        private void PushMotesFrom(Vector3 contact, float strength)
        {
            for (int i = 0; i < _motes.Count; i++)
            {
                var mote = _motes[i];
                float theta = _age * mote.Speed + mote.Phase;
                Vector3 p = mote.U * Mathf.Cos(theta) + mote.V * Mathf.Sin(theta);

                float closeness = Mathf.Clamp01(Vector3.Dot(p, contact));
                mote.Push += Mathf.Pow(closeness, 2.5f) * strength * 0.45f;
            }
        }

        private Vector3 RandomUnitVector()
        {
            // Uniform on the sphere: picking each component uniformly and normalising clumps
            // towards the cube's corners, which puts more orbits on the diagonals.
            float z = Range(-1f, 1f);
            float a = Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
        }
    }
}
