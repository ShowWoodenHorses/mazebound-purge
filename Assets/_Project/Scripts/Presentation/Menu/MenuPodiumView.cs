using Purge.Presentation.Visual;
using UnityEngine;

namespace Purge.Presentation.Menu
{
    /// <summary>
    /// The main menu's 3D stage (stage 11a, Bootstrap scene, far below the levels): a procedural podium, its lights and
    /// camera; the character stands on <see cref="Anchor"/>. Display only — <see cref="MenuPodiumPresenter"/> puts the
    /// player and the weapon there; this poses a held gun after the Animator like the level does
    /// (<see cref="CharacterWeaponRig.PoseGun"/>). Hidden (camera off, stage inactive) outside the main menu.
    /// </summary>
    public sealed class MenuPodiumView : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private GameObject _stage;
        [SerializeField] private Transform _anchor;

        [Tooltip("Degrees per second the character turns on the podium.")]
        [SerializeField] private float _turnSpeed = 8f;

        [Tooltip("The turn swings around the start facing this far each way, degrees.")]
        [SerializeField] private float _turnRange = 25f;

        private CharacterWeaponRig _rig;
        private Transform _gun;
        private WeaponModel _gunModel;
        private bool _alignWithFacing;
        private float _turnTime;
        private MenuPodiumDemo _demo;
        private Vector3 _previewPosition;
        private Quaternion _previewRotation;
        private bool _demonstrating;

        public Transform Anchor => _anchor;
        internal Transform Stage => _stage.transform;

        internal void SetDemo(MenuPodiumDemo demo)
        {
            _demo = demo;
            _previewPosition = _camera.transform.localPosition;
            _previewRotation = _camera.transform.localRotation;
        }

        internal void SetDemonstrating(bool active)
        {
            if (_demo == null || _demonstrating == active) return;
            _demonstrating = active;
            var camera = _camera.transform;
            var focus = new Vector3(0f, 0.55f, 0f);
            var horizontal = new Vector3(3.8f, 0f, 10.5f).normalized;
            const float pitch = 58.5f * Mathf.Deg2Rad;
            const float distance = 21.25f;
            var offset = (horizontal * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * distance;
            camera.localPosition = active ? focus + offset : _previewPosition;
            camera.localRotation = active ? Quaternion.LookRotation(-offset) : _previewRotation;
            _anchor.localRotation = Quaternion.identity;
            _demo.SetActive(active);
        }

        internal Vector3 FloorAtViewport(float x, float y)
        {
            var ray = _camera.ViewportPointToRay(new Vector3(x, y));
            var ground = new Plane(Vector3.up, Stage.position);
            return ground.Raycast(ray, out var distance) ? ray.GetPoint(distance) : Stage.position;
        }

        internal bool IsInFrame(Vector3 position)
        {
            var point = _camera.WorldToViewportPoint(position);
            return point.z > 0f && point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
        }

        public bool IsVisible => _stage != null && _stage.activeSelf;

        public void SetVisible(bool visible)
        {
            if (_stage != null) _stage.SetActive(visible);
            if (_camera != null) _camera.enabled = visible;
        }

        /// <summary>The character's rig and the gun to pose in its hands (null — a melee weapon or none).</summary>
        public void SetPose(CharacterWeaponRig rig, Transform gun, WeaponModel gunModel, bool alignWithFacing)
        {
            _rig = rig;
            _gun = gun;
            _gunModel = gunModel;
            _alignWithFacing = alignWithFacing;
            PoseGun(0f);
        }

        private void LateUpdate()
        {
            if (!IsVisible) return;
            if (!_demonstrating && _anchor != null && _turnRange > 0f && _turnSpeed > 0f)
            {
                _turnTime += Time.unscaledDeltaTime * _turnSpeed / _turnRange;
                _anchor.localRotation = Quaternion.Euler(0f, Mathf.Sin(_turnTime) * _turnRange, 0f);
            }

            PoseGun(Time.unscaledDeltaTime);
            if (_demonstrating) _demo.Tick(Time.unscaledDeltaTime);
        }

        private void PoseGun(float deltaTime)
        {
            if (_rig == null || !_rig.IsValid) return;
            if (_gun != null) _gun.localPosition = Vector3.zero;
            var aim = _alignWithFacing && _anchor != null ? _anchor.forward : Vector3.zero;
            _rig.PoseGun(_gun, _gunModel?.GripLeft, deltaTime, aim, _gunModel?.ShoulderMount == true);
        }
    }
}
