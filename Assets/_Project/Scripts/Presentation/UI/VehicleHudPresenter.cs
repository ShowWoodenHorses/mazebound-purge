using Purge.Gameplay.Level;
using Purge.Gameplay.Vehicles;
using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The vehicle on the HUD (stage 9b): the HP bar over the vehicle the player drives (<see cref="HudScreen.VehicleBar"/>),
    /// hidden on foot.
    /// </summary>
    public sealed class VehicleHudPresenter : ILevelLateTickable
    {
        /// <summary>Metres over the ground the bar stands (over the roof).</summary>
        private const float BarHeight = 2.2f;

        private readonly UIRoot _ui;
        private readonly VehicleSystem _vehicles;
        private readonly TopDownCamera _camera;
        private readonly LevelViewRoot _root;

        public VehicleHudPresenter(UIRoot ui, VehicleSystem vehicles, TopDownCamera camera, LevelViewRoot root)
        {
            _ui = ui;
            _vehicles = vehicles;
            _camera = camera;
            _root = root;
        }

        public void LateTick(float deltaTime)
        {
            var bar = _ui != null && _ui.Hud != null ? _ui.Hud.VehicleBar : null;
            if (bar == null) return;
            var vehicle = _vehicles.Current;
            bar.SetVisible(vehicle != null);
            if (vehicle == null) return;
            bar.SetProgress(vehicle.HealthShare);
            var world = _root.transform.TransformPoint(new Vector3(vehicle.Position.x, BarHeight, vehicle.Position.y));
            bar.Place(_camera.Camera.WorldToScreenPoint(world));
        }
    }
}
