using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// Combat effects, driven by the toolkit's combat log (it reports every attack roll with who, whom and whether it hit -- misses included -- and every heal roll). Nothing in the toolkit
    /// plays effects, and damage is applied BEFORE the attacker's animation starts (the presentation hook runs after Execute), so this owns the timing the eye expects:
    /// <list type="bullet">
    /// <item>a melee hit lands <see cref="MeleeHitDelay"/> after the swing starts: an impact burst on the target;</item>
    /// <item>a ranged attack releases when the animation does (the bow clip's release is frame 29 of 46 at 30 fps = 0.97 s; a spell gesture is quicker), launches an arrow or a flaming
    /// bolt that flies to the target at <see cref="ProjectileSpeed"/>, and bursts on arrival (or kicks up dust beside the target on a miss);</item>
    /// <item>a heal roll shows a heal burst on the target.</item>
    /// </list>
    /// <see cref="HitDelay"/> is the same timing as a pure function: <c>ModelActionCues</c> uses it so the target's hit reaction plays when the blow or arrow LANDS, not at the instant the damage number changes.
    /// </summary>
    public sealed class CombatFx : MonoBehaviour
    {
        public const float MeleeHitDelay = 0.35f, BowRelease = 0.97f, CastRelease = 0.45f, ProjectileSpeed = 28f, RangedThreshold = 3.2f, FxLifetime = 4f;

        [SerializeField] private GameObject arrowPrefab, boltPrefab, impactSmallPrefab, impactLargePrefab, healPrefab, missPrefab, boltImpactPrefab;

        private sealed class Projectile { public Transform Transform; public Vector3 From, To; public float Start, Duration; public bool Hit, Crit, Bolt; }

        private readonly List<(float at, Action action)> timeline = new();
        private readonly List<Projectile> flying = new();
        private Quaternion arrowAxisFix = Quaternion.identity;
        private bool arrowAxisKnown;

        /// <summary>Hand this to the ability resolution as its combat log.</summary>
        public CombatLog Log { get; } = new();
        public int ImpactsSpawned { get; private set; }
        public int ProjectilesSpawned { get; private set; }
        public int HealsSpawned { get; private set; }
        public int MissesSpawned { get; private set; }

        private void Awake() => Log.EntryLogged += OnEntry;
        private void OnDestroy() => Log.EntryLogged -= OnEntry;

        // ------------------------------------------------------------------ timing (pure)
        public static float ReleaseSeconds(bool bowUser) => bowUser ? BowRelease : CastRelease;

        /// <summary>Seconds from the moment an attack is committed until it LANDS on a target <paramref name="distance"/> metres away.</summary>
        public static float HitDelay(float distance, bool bowUser) => distance <= RangedThreshold ? MeleeHitDelay : ReleaseSeconds(bowUser) + distance / ProjectileSpeed;

        public static float HitDelayBetween(RuntimeEntityId source, RuntimeEntityId target)
        {
            var from = VikingGame.FindAgent(source); var to = VikingGame.FindAgent(target);
            if (from == null || to == null) return MeleeHitDelay;
            return HitDelay(Vector3.Distance(from.transform.position, to.transform.position), IsBowUser(from));
        }

        private static bool IsBowUser(CRPG.Gameplay.CharacterAgent agent) => agent.name.Contains("Sigrid") || agent.name.Contains("Archer");
        private static bool IsCaster(CRPG.Gameplay.CharacterAgent agent) => agent.name.Contains("Shaman");

        // ------------------------------------------------------------------ log -> effects
        private void OnEntry(CombatLogEntry entry)
        {
            try
            {
                if (entry is AttackRollLogEntry attack) Attack(attack);
                else if (entry is RollLogEntry roll && roll.Result.Request.Purpose == RollPurpose.Generic && roll.Target.IsValid) Heal(roll.Target);
            }
            catch (Exception ex) { Debug.LogException(ex); }   // effects must never fault the fight
        }

        private void Attack(AttackRollLogEntry attack)
        {
            var from = VikingGame.FindAgent(attack.Source); var to = VikingGame.FindAgent(attack.Target);
            if (from == null || to == null) return;
            var distance = Vector3.Distance(from.transform.position, to.transform.position);

            if (distance > RangedThreshold)
            {
                var bolt = IsCaster(from) && !IsBowUser(from);
                var origin = from.transform.position + Vector3.up * 1.5f + from.transform.forward * 0.5f;
                var chest = to.transform.position + Vector3.up * 1.1f;
                if (!attack.IsHit) chest += (Vector3.Cross(Vector3.up, chest - origin).normalized * UnityEngine.Random.Range(-1f, 1f) + Vector3.up * UnityEngine.Random.Range(-0.3f, 0.6f)) * 1.1f;
                var hit = attack.IsHit; var crit = attack.IsCriticalHit; var release = ReleaseSeconds(IsBowUser(from));
                At(release, () => Launch(origin, chest, hit, crit, bolt));
            }
            else if (attack.IsHit)
            {
                var target = to; var crit = attack.IsCriticalHit;
                At(MeleeHitDelay, () => { if (target != null) Impact(target.transform.position + Vector3.up * 1.1f, crit); });
            }
        }

        private void Heal(RuntimeEntityId target)
        {
            var agent = VikingGame.FindAgent(target);
            if (agent == null) return;
            At(0.3f, () => { if (agent != null) { Spawn(healPrefab, agent.transform.position + Vector3.up * 0.2f); HealsSpawned++; } });
        }

        // ------------------------------------------------------------------ spawning
        private void At(float seconds, Action action) => timeline.Add((Time.time + seconds, action));

        private void Impact(Vector3 position, bool crit)
        {
            Spawn(crit && impactLargePrefab != null ? impactLargePrefab : impactSmallPrefab, position);
            ImpactsSpawned++;
        }

        private GameObject Spawn(GameObject prefab, Vector3 position)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, position, Quaternion.identity);
            Destroy(go, FxLifetime);
            return go;
        }

        private void Launch(Vector3 from, Vector3 to, bool hit, bool crit, bool bolt)
        {
            var prefab = bolt ? boltPrefab : arrowPrefab;
            if (prefab == null) { if (hit) Impact(to, crit); return; }
            var go = Instantiate(prefab, from, Orientation(to - from, bolt));
            ProjectilesSpawned++;
            flying.Add(new Projectile { Transform = go.transform, From = from, To = to, Start = Time.time, Duration = Mathf.Max(0.05f, Vector3.Distance(from, to) / ProjectileSpeed), Hit = hit, Crit = crit, Bolt = bolt });
        }

        // the arrow model's long axis is not world forward: find it once from the prefab's bounds and align it with the flight direction
        private Quaternion Orientation(Vector3 direction, bool bolt)
        {
            if (direction.sqrMagnitude < 1e-4f) return Quaternion.identity;
            if (bolt) return Quaternion.LookRotation(direction);
            if (!arrowAxisKnown)
            {
                arrowAxisKnown = true;
                var renderers = arrowPrefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                    var s = b.size; var axis = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
                    arrowAxisFix = Quaternion.FromToRotation(axis, Vector3.forward);
                }
            }

            return Quaternion.LookRotation(direction) * arrowAxisFix;
        }

        private void Update()
        {
            var now = Time.time;
            for (var i = timeline.Count - 1; i >= 0; i--)
            {
                if (now < timeline[i].at) continue;
                var action = timeline[i].action; timeline.RemoveAt(i);
                action();
            }

            for (var i = flying.Count - 1; i >= 0; i--)
            {
                var p = flying[i];
                if (p.Transform == null) { flying.RemoveAt(i); continue; }
                var u = Mathf.Clamp01((now - p.Start) / p.Duration);
                p.Transform.position = Vector3.Lerp(p.From, p.To, u);
                if (u < 1f) continue;
                flying.RemoveAt(i);
                Destroy(p.Transform.gameObject);
                if (p.Hit) { Impact(p.To, p.Crit); if (p.Bolt && boltImpactPrefab != null) Spawn(boltImpactPrefab, p.To); }
                else { Spawn(missPrefab, new Vector3(p.To.x, Mathf.Max(0.05f, p.To.y - 1f), p.To.z)); MissesSpawned++; }
            }
        }
    }
}
