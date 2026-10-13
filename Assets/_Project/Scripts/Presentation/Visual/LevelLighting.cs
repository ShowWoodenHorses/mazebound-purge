using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Level;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Applies the theme's lighting mood (<see cref="ThemeLighting"/>) to the level: flat ambient, a weak directional
    /// "moon" without shadows (no realtime shadows at all: an extra pass over the whole scene on Android and WebGL),
    /// and the lantern around the player — shader globals read by MazeLighting.cginc, updated every frame from the
    /// player view. Must be registered after <see cref="PlayerViewPresenter"/> (late tick order).
    /// </summary>
    public sealed class LevelLighting : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private static readonly int LanternPositionId = Shader.PropertyToID("_MazeLanternPosition");
        private static readonly int LanternColorId = Shader.PropertyToID("_MazeLanternColor");
        private static readonly int LightCeilingId = Shader.PropertyToID("_MazeLightCeiling");
        private static readonly int FrostColorId = Shader.PropertyToID("_MazeFrostColor");
        private static readonly int FrostParamsId = Shader.PropertyToID("_MazeFrostParams");

        private readonly LevelData _level;
        private readonly LevelViewRoot _root;
        private readonly PlayerViewPresenter _player;
        private ThemeLighting _lighting;

        public LevelLighting(LevelData level, LevelViewRoot root, PlayerViewPresenter player)
        {
            _level = level;
            _root = root;
            _player = player;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _lighting = _level.VisualTheme != null ? _level.VisualTheme.Lighting : new ThemeLighting();

            // The Game scene is the active one, so its render settings are used.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = _lighting.Ambient;

            var moon = _root.Moon;
            if (moon != null)
            {
                moon.shadows = LightShadows.None;
                moon.color = _lighting.MoonColor;
                moon.intensity = _lighting.MoonIntensity;
                moon.enabled = _lighting.MoonIntensity > 0f;
            }

            // The sources' light on floor and walls fades with height (MazeLighting.cginc, MazeLightCeiling).
            Shader.SetGlobalVector(LightCeilingId, new Vector4(_lighting.LightFadeStart,
                1f / Mathf.Max(_lighting.LightFadeLength, 0.01f), Mathf.Clamp01(_lighting.LightTopShare), 0f));

            // Frost of the characters (Maze/Lit): the amount is per model (HitFlash), colour and shape are global.
            var weather = _level.VisualTheme != null ? _level.VisualTheme.Weather : new ThemeWeather();
            var frost = QualitySettings.activeColorSpace == ColorSpace.Linear ? weather.FrostColor.linear : weather.FrostColor;
            Shader.SetGlobalVector(FrostColorId, new Vector4(frost.r, frost.g, frost.b, 1f));
            Shader.SetGlobalVector(FrostParamsId, new Vector4(weather.FrostSharpness, weather.FrostRim, 0f, 0f));

            ClearLantern();
            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            var view = _player.View;
            if (_lighting == null || view == null)
                return;

            var position = view.transform.position + Vector3.up * _lighting.LanternHeight;
            Shader.SetGlobalVector(LanternPositionId, new Vector4(position.x, position.y, position.z,
                1f / Mathf.Max(_lighting.LanternRadius, 0.01f)));
            var lantern = QualitySettings.activeColorSpace == ColorSpace.Linear ? _lighting.LanternColor.linear : _lighting.LanternColor;
            var color = lantern * _lighting.LanternIntensity;
            Shader.SetGlobalVector(LanternColorId, new Vector4(color.r, color.g, color.b, 0f));
        }

        public void Dispose()
        {
            ClearLantern();
            Shader.SetGlobalVector(LightCeilingId, Vector4.zero);
            Shader.SetGlobalVector(FrostColorId, Vector4.zero);
            Shader.SetGlobalVector(FrostParamsId, Vector4.zero);
        }

        private static void ClearLantern()
        {
            Shader.SetGlobalVector(LanternColorId, Vector4.zero);
        }
    }
}
