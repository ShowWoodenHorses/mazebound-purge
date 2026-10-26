using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// The arena's gates (stage 8a): sections of the theme's gate (<c>VisualTheme.ArenaGate</c>) across every entry of the
    /// arena, made at load and hidden under the floor; they rise when <see cref="BossSystem.GatesClosed"/> and sink when
    /// the boss dies (<see cref="MoveTime"/>). The gameplay's gates are the blocked cells, not these.
    /// </summary>
    public sealed class ArenaGatesView : ILevelLoadStep, ILevelLateTickable, IViewWarmup
    {
        /// <summary>Seconds a gate takes to rise or sink.</summary>
        public const float MoveTime = 0.35f;

        /// <summary>How deep an open gate hides, m.</summary>
        private const float Depth = 2.8f;

        private readonly LevelData _level;
        private readonly BossSystem _bosses;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly List<Transform> _sections = new List<Transform>();
        private float _raised; // 0 — down, 1 — up

        public ArenaGatesView(LevelData level, BossSystem bosses, IAssetOwner assets, LevelViewRoot root)
        {
            _level = level;
            _bosses = bosses;
            _assets = assets;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies; // after BossSystem found the entries

        public int SectionCount => _sections.Count;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var theme = _level.VisualTheme;
            var look = theme != null ? theme.ArenaGate : null;
            if (_bosses.Entries.Count == 0 || look == null || !look.RuntimeKeyIsValid()) return;
            var prefab = await _assets.LoadAsync<GameObject>(look, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (prefab == null) return;

            var parent = new GameObject("Arena Gates").transform;
            parent.SetParent(_root != null ? _root.transform : null, false);
            var length = theme.ArenaGateLength;
            foreach (var entry in _bosses.Entries)
                PlaceEntry(prefab, parent, entry, length);
            Pose();
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_sections.Count > 0) objects.Add(_sections[0].gameObject);
        }

        public void LateTick(float deltaTime)
        {
            if (_sections.Count == 0) return;
            var target = _bosses.GatesClosed ? 1f : 0f;
            if (Mathf.Approximately(_raised, target)) return;
            _raised = Mathf.MoveTowards(_raised, target, deltaTime / MoveTime);
            Pose();
        }

        /// <summary>Sections along the entry's width, in the middle of its depth through the ring.</summary>
        private void PlaceEntry(GameObject prefab, Transform parent, ArenaEntry entry, float sectionLength)
        {
            var gap = entry.Gap;
            var alongX = entry.Side == Direction.North || entry.Side == Direction.South;
            var width = alongX ? gap.Width : gap.Height;
            var count = Mathf.Max(1, Mathf.RoundToInt(width / sectionLength));
            var section = width / (float)count;
            for (var i = 0; i < count; i++)
            {
                var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
                instance.name = $"Gate {entry.Side} {i + 1}";
                var t = instance.transform;
                // The model spans along its −X from its pivot: right end of the section; turned 90° it spans along +Z.
                Vector3 position;
                if (alongX)
                {
                    var middle = gap.Y + (gap.Height - 1) * 0.5f;
                    position = new Vector3(gap.X - 0.5f + section * (i + 1), 0f, middle);
                    t.localRotation = Quaternion.identity;
                }
                else
                {
                    var middle = gap.X + (gap.Width - 1) * 0.5f;
                    position = new Vector3(middle, 0f, gap.Y - 0.5f + section * i);
                    t.localRotation = Quaternion.Euler(0f, 90f, 0f);
                }

                t.localPosition = position;
                t.localScale = new Vector3(section / sectionLength, 1f, 1f);
                _sections.Add(t);
            }
        }

        private void Pose()
        {
            var eased = _raised * _raised * (3f - 2f * _raised);
            var y = -Depth * (1f - eased);
            foreach (var section in _sections)
            {
                var position = section.localPosition;
                position.y = y;
                section.localPosition = position;
                var shown = _raised > 0.001f;
                if (section.gameObject.activeSelf != shown) section.gameObject.SetActive(shown);
            }
        }
    }
}
