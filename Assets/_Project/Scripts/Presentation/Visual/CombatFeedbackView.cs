using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using Maze.Gameplay.Zombies;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Health bars over damaged zombies and flying numbers — damage to zombies and to the player, medkit healing.
    /// Display only; every colour and size is in <see cref="CombatFeedback"/> (<see cref="CombatVisualDefinition"/>).
    /// <list type="bullet">
    /// <item>A zombie's bar appears with its hit (<see cref="ZombieViewPresenter.DamageShown"/>, in time with the
    /// melee clip), stays <see cref="CombatFeedback.BarShowTime"/> after the last hit, then fades; the lost part shows a
    /// moment and shrinks. Hidden while the zombie's view is hidden; a killed zombie's bar goes at once.</item>
    /// <item>A number appears small at the target, grows while rising and fades (<see cref="CombatFeedback.NumberLifetime"/>).
    /// Zombie numbers are hidden with the zombie's view; quick hits in a row are spread sideways.</item>
    /// </list>
    /// Everything is one dynamic mesh with the <c>Maze/Overhead</c> material (one draw call): fixed pools, no
    /// GameObject per bar or number, no Canvas, no allocations during play; the mesh is rebuilt only while something
    /// shows. The camera never turns, so quads lie flat with "up" = +Z.
    /// </summary>
    public sealed class CombatFeedbackView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        public const int MaxBars = 16;
        public const int MaxNumbers = 24;

        private const int MaxChars = 4; // "+100"
        private const int QuadsPerBar = 4; // border, background, lost part, fill
        private const int MaxQuads = MaxBars * QuadsPerBar + MaxNumbers * MaxChars;
        private const float Height = 2f; // about the head: follows it on screen with the perspective
        private const float SpreadWindow = 0.35f; // share of the lifetime: a newer number at the same target is spread

        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineId = Shader.PropertyToID("_Outline");
        // Offsets of numbers that follow each other at one target, × NumberSpread: aside and a bit lower, so "12" and "8"
        // never read as "128".
        private static readonly Vector2[] SpreadPattern =
        {
            new Vector2(0f, 0f), new Vector2(1f, -0.35f), new Vector2(-1f, -0.35f), new Vector2(0.5f, -0.7f), new Vector2(-0.5f, -0.7f),
        };

        private readonly LevelData _level;
        private readonly CombatVisualDefinition _combatVisual;
        private readonly ZombieViewPresenter _zombieViews;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly EntityViewRegistry _views;
        private readonly LevelViewRoot _root;

        private readonly Bar[] _bars = new Bar[MaxBars];
        private readonly Number[] _numbers = new Number[MaxNumbers];
        private readonly NumberGlyph[] _glyphs = new NumberGlyph[128];
        private readonly bool[] _hasGlyph = new bool[128];
        private readonly char[] _chars = new char[MaxChars];
        private readonly Vector3[] _vertices = new Vector3[MaxQuads * 4];
        private readonly Color32[] _colors = new Color32[MaxQuads * 4];
        private readonly Vector3[] _uvs = new Vector3[MaxQuads * 4];
        private readonly int[] _indices = new int[MaxQuads * 6];

        private CombatFeedback _settings;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Color32 _barBorder, _barBackground, _barFill, _barTrail, _zombieDamage, _playerDamage, _heal;
        private int _lastHealth;
        private int _quads;
        private bool _subscribed;

        public CombatFeedbackView(LevelData level, CombatVisualDefinition combatVisual, ZombieViewPresenter zombieViews,
            PlayerSystem player, PlayerHealth health, EntityViewRegistry views, LevelViewRoot root)
        {
            _level = level;
            _combatVisual = combatVisual;
            _zombieViews = zombieViews;
            _player = player;
            _health = health;
            _views = views;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>Bars shown now (tests).</summary>
        public int ActiveBars
        {
            get
            {
                var count = 0;
                foreach (var bar in _bars)
                    if (bar.Zombie != null) count++;
                return count;
            }
        }

        /// <summary>Numbers alive now (tests).</summary>
        public int ActiveNumbers
        {
            get
            {
                var count = 0;
                foreach (var number in _numbers)
                    if (number.Active) count++;
                return count;
            }
        }

        /// <summary>Quads drawn in the last frame (tests).</summary>
        public int DrawnQuads => _renderer != null && _renderer.enabled ? _quads : 0;

        /// <summary>The text of every number alive now, e.g. "12", "+40" (tests; allocates).</summary>
        public List<string> NumberTexts()
        {
            var texts = new List<string>();
            foreach (var number in _numbers)
                if (number.Active)
                    texts.Add(new string(_chars, 0, Format(number.Value, number.Heal)));
            return texts;
        }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _settings = _combatVisual != null ? _combatVisual.Feedback : null;
            if (_settings == null || _settings.Material == null || _settings.Glyphs.Length == 0)
                return UniTask.CompletedTask;

            foreach (var glyph in _settings.Glyphs)
            {
                if (glyph.Character >= _glyphs.Length) continue;
                _glyphs[glyph.Character] = glyph;
                _hasGlyph[glyph.Character] = true;
            }

            _barBorder = ToVertex(_settings.BarBorderColor);
            _barBackground = ToVertex(_settings.BarBackgroundColor);
            _barFill = ToVertex(_settings.BarFillColor);
            _barTrail = ToVertex(_settings.BarTrailColor);
            _zombieDamage = ToVertex(_settings.ZombieDamageColor);
            _playerDamage = ToVertex(_settings.PlayerDamageColor);
            _heal = ToVertex(_settings.HealColor);

            for (var i = 0; i < MaxQuads; i++)
            {
                var v = i * 4;
                var t = i * 6;
                _indices[t] = v;
                _indices[t + 1] = v + 2;
                _indices[t + 2] = v + 1;
                _indices[t + 3] = v + 1;
                _indices[t + 4] = v + 2;
                _indices[t + 5] = v + 3;
            }

            CreateRenderer();
            _lastHealth = _health.Current;

            if (!_subscribed)
            {
                _zombieViews.DamageShown += OnZombieDamageShown;
                _health.Changed += OnHealthChanged;
                _subscribed = true;
            }

            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (_renderer == null) return;

            _quads = 0;
            UpdateBars(deltaTime);
            UpdateNumbers(deltaTime);

            if (_quads == 0)
            {
                if (_renderer.enabled) _renderer.enabled = false;
                return;
            }

            Upload();
            if (!_renderer.enabled) _renderer.enabled = true;
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            if (_renderer == null) return;

            // One invisible glyph, so the shader and the atlas are drawn while loading.
            _quads = 0;
            var glyph = FirstGlyph();
            AddQuad(Vector2.zero, new Vector2(0.1f, 0.1f), glyph.Uv, solid: false, new Color32(0, 0, 0, 0));
            Upload();
            _quads = 0;
            objects.Add(_renderer.gameObject);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _zombieViews.DamageShown -= OnZombieDamageShown;
                _health.Changed -= OnHealthChanged;
                _subscribed = false;
            }

            if (_renderer != null) UnityObjects.DestroyObjectOf(_renderer);
            UnityObjects.Destroy(_mesh);
            _renderer = null;
            _mesh = null;
            Array.Clear(_bars, 0, _bars.Length);
            Array.Clear(_numbers, 0, _numbers.Length);
        }

        // ---- Events ----

        private void OnZombieDamageShown(ZombieRuntime zombie, float damage, float healthAfter)
        {
            if (_renderer == null) return;

            _views.TryGet(zombie.Id, out var view);
            AddNumber(zombie.Position, Mathf.Max(1, Mathf.RoundToInt(damage)), heal: false, _zombieDamage, view);

            var index = FindBar(zombie);
            if (healthAfter <= 0f)
            {
                if (index >= 0) _bars[index] = default;
                return;
            }

            var max = Mathf.Max(zombie.Definition.MaxHp, 1f);
            var fraction = Mathf.Clamp01(healthAfter / max);
            if (index < 0)
            {
                index = FreeBar();
                _bars[index] = new Bar { Zombie = zombie, View = view, Shown = Mathf.Clamp01((healthAfter + damage) / max) };
            }

            ref var bar = ref _bars[index];
            if (bar.Trail < bar.Shown) bar.Trail = bar.Shown; // the lost part starts where the bar was
            bar.Shown = fraction;
            bar.TrailWait = _settings.BarTrailDelay;
            bar.Left = _settings.BarShowTime;
            bar.View = view;
        }

        private void OnHealthChanged(int current, int max)
        {
            var delta = current - _lastHealth;
            _lastHealth = current;
            if (_renderer == null || delta == 0 || !_player.IsSpawned) return;

            if (delta < 0) AddNumber(_player.Position, -delta, heal: false, _playerDamage, null);
            else AddNumber(_player.Position, delta, heal: true, _heal, null);
        }

        // ---- Bars ----

        private int FindBar(ZombieRuntime zombie)
        {
            for (var i = 0; i < _bars.Length; i++)
                if (ReferenceEquals(_bars[i].Zombie, zombie))
                    return i;
            return -1;
        }

        /// <summary>A free slot, or the one closest to disappearing.</summary>
        private int FreeBar()
        {
            var best = 0;
            for (var i = 0; i < _bars.Length; i++)
            {
                if (_bars[i].Zombie == null) return i;
                if (_bars[i].Left < _bars[best].Left) best = i;
            }
            return best;
        }

        private void UpdateBars(float deltaTime)
        {
            var trailSpeed = 1f / Mathf.Max(_settings.BarTrailTime, 0.01f);
            for (var i = 0; i < _bars.Length; i++)
            {
                ref var bar = ref _bars[i];
                if (bar.Zombie == null) continue;

                bar.Left -= deltaTime;
                if (bar.Left <= 0f)
                {
                    bar = default;
                    continue;
                }

                if (bar.TrailWait > 0f) bar.TrailWait -= deltaTime;
                else bar.Trail = Mathf.MoveTowards(bar.Trail, bar.Shown, trailSpeed * deltaTime);

                if (bar.View == null || !bar.View.IsVisible) continue;

                var alpha = Mathf.Clamp01(bar.Left / Mathf.Max(_settings.BarFadeTime, 0.01f));
                DrawBar(bar.Zombie.Position, bar.Shown, bar.Trail, alpha);
            }
        }

        private void DrawBar(Vector2 position, float shown, float trail, float alpha)
        {
            var width = _settings.BarWidth;
            var height = _settings.BarHeight;
            var border = _settings.BarBorder;
            var center = position + new Vector2(0f, _settings.BarOffset);
            var min = center - new Vector2(width, height) * 0.5f;
            var max = min + new Vector2(width, height);

            if (border > 0f)
                AddQuad(min - Vector2.one * border, max + Vector2.one * border, default, true, Fade(_barBorder, alpha));
            AddQuad(min, max, default, true, Fade(_barBackground, alpha));
            if (trail > shown)
                AddQuad(new Vector2(min.x + width * shown, min.y), new Vector2(min.x + width * trail, max.y), default, true,
                    Fade(_barTrail, alpha));
            if (shown > 0f)
                AddQuad(min, new Vector2(min.x + width * shown, max.y), default, true, Fade(_barFill, alpha));
        }

        // ---- Numbers ----

        private void AddNumber(Vector2 position, int value, bool heal, Color32 color, EntityView view)
        {
            // Numbers at the same target still young are spread sideways; the oldest slot is reused when all are busy.
            var spread = 0;
            var slot = -1;
            var oldest = 0;
            var oldestAge = -1f;
            var lifetime = Mathf.Max(_settings.NumberLifetime, 0.05f);
            for (var i = 0; i < _numbers.Length; i++)
            {
                ref var number = ref _numbers[i];
                if (!number.Active)
                {
                    if (slot < 0) slot = i;
                    continue;
                }

                if (number.Age > oldestAge)
                {
                    oldestAge = number.Age;
                    oldest = i;
                }
                if (ReferenceEquals(number.View, view) && number.Heal == heal && number.Age < lifetime * SpreadWindow)
                    spread = Mathf.Max(spread, number.Spread + 1);
            }

            if (slot < 0) slot = oldest;
            _numbers[slot] = new Number
            {
                Active = true,
                Position = position,
                Value = Mathf.Clamp(value, 0, 999),
                Heal = heal,
                Color = color,
                View = view,
                HasView = view != null,
                Spread = spread,
            };
        }

        private void UpdateNumbers(float deltaTime)
        {
            var lifetime = Mathf.Max(_settings.NumberLifetime, 0.05f);
            var growShare = Mathf.Clamp(_settings.NumberGrowShare, 0.05f, 1f);
            for (var i = 0; i < _numbers.Length; i++)
            {
                ref var number = ref _numbers[i];
                if (!number.Active) continue;

                number.Age += deltaTime;
                var t = number.Age / lifetime;
                if (t >= 1f)
                {
                    number = default;
                    continue;
                }

                // A zombie's number is hidden with its view (also when the view is gone).
                if (number.HasView && (number.View == null || !number.View.IsVisible)) continue;

                var grow = EaseOut(Mathf.Clamp01(t / growShare));
                var size = Mathf.Lerp(_settings.NumberStartSize, _settings.NumberEndSize, grow);
                var alpha = t <= growShare ? 1f : 1f - (t - growShare) / (1f - growShare + 1e-4f);
                var offset = SpreadPattern[number.Spread % SpreadPattern.Length] * _settings.NumberSpread;
                var center = number.Position + offset + new Vector2(0f, _settings.NumberOffset + _settings.NumberRise * EaseOut(t));
                DrawNumber(center, size, number.Value, number.Heal, Fade(number.Color, alpha));
            }
        }

        private void DrawNumber(Vector2 center, float size, int value, bool heal, Color32 color)
        {
            var count = Format(value, heal);
            var width = 0f;
            for (var i = 0; i < count; i++)
                if (_hasGlyph[_chars[i]]) width += _glyphs[_chars[i]].Advance;

            var pen = center.x - width * size * 0.5f;
            var baseline = center.y - size * 0.5f;
            for (var i = 0; i < count; i++)
            {
                if (!_hasGlyph[_chars[i]]) continue;
                var glyph = _glyphs[_chars[i]];
                var quad = glyph.Quad;
                AddQuad(new Vector2(pen + quad.x * size, baseline + quad.y * size),
                    new Vector2(pen + quad.z * size, baseline + quad.w * size), glyph.Uv, false, color);
                pen += glyph.Advance * size;
            }
        }

        /// <summary>Writes the number's characters into <see cref="_chars"/>; returns their count.</summary>
        private int Format(int value, bool heal)
        {
            var count = 0;
            if (heal) _chars[count++] = '+';
            var start = count;
            do
            {
                _chars[count++] = (char)('0' + value % 10);
                value /= 10;
            } while (value > 0 && count < MaxChars);

            Array.Reverse(_chars, start, count - start);
            return count;
        }

        // ---- Mesh ----

        private void AddQuad(Vector2 min, Vector2 max, Rect uv, bool solid, Color32 color)
        {
            if (_quads >= MaxQuads) return;

            var v = _quads * 4;
            _vertices[v] = new Vector3(min.x, Height, min.y);
            _vertices[v + 1] = new Vector3(max.x, Height, min.y);
            _vertices[v + 2] = new Vector3(min.x, Height, max.y);
            _vertices[v + 3] = new Vector3(max.x, Height, max.y);

            var flag = solid ? 1f : 0f;
            _uvs[v] = new Vector3(uv.xMin, uv.yMin, flag);
            _uvs[v + 1] = new Vector3(uv.xMax, uv.yMin, flag);
            _uvs[v + 2] = new Vector3(uv.xMin, uv.yMax, flag);
            _uvs[v + 3] = new Vector3(uv.xMax, uv.yMax, flag);

            _colors[v] = _colors[v + 1] = _colors[v + 2] = _colors[v + 3] = color;
            _quads++;
        }

        private void Upload()
        {
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            var vertexCount = _quads * 4;
            // Indices first shrink, so they never point past the vertices being set.
            _mesh.SetIndices(_indices, 0, 0, MeshTopology.Triangles, 0, false);
            _mesh.SetVertices(_vertices, 0, vertexCount, flags);
            _mesh.SetUVs(0, _uvs, 0, vertexCount, flags);
            _mesh.SetColors(_colors, 0, vertexCount, flags);
            _mesh.SetIndices(_indices, 0, _quads * 6, MeshTopology.Triangles, 0, false);
        }

        private void CreateRenderer()
        {
            _mesh = new Mesh { name = "CombatFeedback" };
            _mesh.MarkDynamic();
            var size = _level.Geometry != null
                ? new Vector3(_level.Geometry.Width + 4f, Height * 2f + 2f, _level.Geometry.Height + 4f)
                : new Vector3(1000f, 10f, 1000f);
            _mesh.bounds = new Bounds(new Vector3(size.x * 0.5f - 2f, Height, size.z * 0.5f - 2f), size);

            var go = new GameObject("CombatFeedback");
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _settings.Material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false;

            var block = new MaterialPropertyBlock();
            block.SetColor(OutlineColorId, _settings.OutlineColor);
            block.SetFloat(OutlineId, _settings.OutlineWidth);
            _renderer.SetPropertyBlock(block);
        }

        private NumberGlyph FirstGlyph()
        {
            for (var i = 0; i < _hasGlyph.Length; i++)
                if (_hasGlyph[i]) return _glyphs[i];
            return default;
        }

        private static Color32 ToVertex(Color color)
        {
            // Vertex colours are not converted by Unity: the palette is sRGB, the project renders in Linear.
            var value = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            value.a = color.a;
            return value;
        }

        private static Color32 Fade(Color32 color, float alpha)
        {
            color.a = (byte)(color.a * Mathf.Clamp01(alpha));
            return color;
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        private struct Bar
        {
            public ZombieRuntime Zombie; // null = free
            public EntityView View;
            public float Shown;
            public float Trail;
            public float TrailWait;
            public float Left;
        }

        private struct Number
        {
            public bool Active;
            public Vector2 Position;
            public int Value;
            public bool Heal;
            public Color32 Color;
            public EntityView View;
            public bool HasView;
            public int Spread;
            public float Age;
        }
    }
}
