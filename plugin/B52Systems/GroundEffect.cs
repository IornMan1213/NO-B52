using System.Collections.Generic;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Ground effect (the game has none). Within about a wingspan of the ground the ground blocks the downwash, so a
    /// wing makes more lift at the same angle: the real B-52 lifts off almost level on its 6-degree wing incidence,
    /// while in the game it needed a 6-9 degree rotation and 170-175 kt. This adds a share of the wings' own lift from
    /// the last physics step: Plugin.GroundLift at wheels-on-ground (wing 4.5 m up), fading as McCormick's
    /// induced-drag ratio phi = (16h/b)^2 / (1 + (16h/b)^2) recovers towards 1: a quarter of it left at 11 m, gone by
    /// about 30 m. Added through the centre of mass, so it doesn't change the trim. Off while braking.
    /// </summary>
    public class GroundEffect : MonoBehaviour
    {
        private const float Span = 56.4f, WingHeight = 4.5f, MinSpeed = 20f;

        private Aircraft aircraft;
        private Rigidbody rb;
        private readonly List<AeroPart> wings = new List<AeroPart>();
        private float phi0;

        internal float Height, Lift, Extra;   // last step, for the test driver

        internal static float Phi(float h) { float x = 16f * h / Span; return x * x / (1f + x * x); }

        /// <summary>Share of the full bonus at wing height h (1 on the ground, 0 high up).</summary>
        internal static float Share(float h, float phiGround) => Mathf.Clamp01((1f - Phi(h)) / (1f - phiGround));

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            rb = aircraft ? aircraft.rb : null;
            foreach (var p in GetComponentsInChildren<AeroPart>(true))
                if (p.name.StartsWith("wing") || p.name.StartsWith("flap")) wings.Add(p);
            phi0 = Phi(WingHeight);
            if (!aircraft || wings.Count == 0) Destroy(this);
        }

        private void FixedUpdate()
        {
            Extra = 0f;
            if (!rb || !aircraft.LocalSim || aircraft.disabled || Plugin.GroundLift.Value <= 0f) return;
            var inp = aircraft.GetInputs();
            if (inp != null && inp.brake > 0.5f) return;            // braking: the real one dumps lift with its spoilers
            Height = WingHeight + Mathf.Max(0f, aircraft.radarAlt);
            float share = Share(Height, phi0);
            var v = rb.velocity; float speed = v.magnitude;
            if (share < 0.01f || speed < MinSpeed) return;
            var dir = v / speed;
            var lift = Vector3.zero;
            foreach (var p in wings)
            {
                if (!p || p.rb != rb) continue;                    // a wing that broke off flies on its own
                var f = AeroCentres.Force(p);
                lift += f - Vector3.Dot(f, dir) * dir;             // the part of the aero force across the airflow
            }
            Lift = lift.magnitude;
            var extra = lift * (Plugin.GroundLift.Value * share);
            Extra = extra.magnitude;
            rb.AddForce(extra);
        }
    }
}
