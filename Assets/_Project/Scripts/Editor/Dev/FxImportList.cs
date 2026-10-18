using System;
using System.Collections.Generic;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Which effects of the packs in Import/FX the project takes (stage Э1): source prefabs are dragged in here and
    /// Purge → Dev → Build Pack Effects (<see cref="FxPackBuilder"/>) makes cleaned copies of them in
    /// Art/Effects/&lt;pack&gt;/. Editor-only: nothing at runtime references the list; the copies are what the game uses.
    /// </summary>
    [CreateAssetMenu(fileName = "FxImport", menuName = "Purge/Dev/FX Import List")]
    internal sealed class FxImportList : ScriptableObject
    {
        public const string Path = "Assets/_Project/Art/Effects/FxImport.asset";

        [SerializeField] private List<Entry> _effects = new List<Entry>();

        public List<Entry> Effects => _effects;

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("A prefab under Assets/_Project/Import/FX.")]
            public GameObject Source;

            [Tooltip("Name of the copy; empty = the source's name.")]
            public string Name;

            [Tooltip("Size of the copy against the source (pack effects are made for other cameras).")]
            [Min(0.05f)] public float Scale = 1f;

            [Tooltip("Cells the copy covers at its size (explosions are scaled to their radius by it); 0 = never scaled. " +
                     "A looping stream: cells it reaches (fitted to the weapon's range by the particles' lifetime).")]
            [Min(0f)] public float Radius;

            [Tooltip("Continuous (streams of flamethrowers): keeps looping while the weapon fires, then fades out.")]
            public bool Loop;

            [Tooltip("Seen from above: nothing drifts up (gravity and upward velocity off) — rising particles grow " +
                     "towards the camera into huge blobs (clouds of smoke). Streams (Loop) are always flat.")]
            public bool Flat;

            [Tooltip("Recolour (alpha 0 = keep the pack's colours): every particle's colour becomes this times its own " +
                     "brightness — one pack effect in several colours (streams of acid, frost, gas).")]
            public Color Tint = new Color(1f, 1f, 1f, 0f);

            [Tooltip("Share of the source's particles kept (bursts and emission rate; at least one).")]
            [Range(0.1f, 1f)] public float Particles = FxPackBuilder.DefaultParticleShare;
        }
    }
}
