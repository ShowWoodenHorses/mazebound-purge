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

        public Transform Anchor => _anchor;

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
            if (_anchor != null && _turnRange > 0f && _turnSpeed > 0f)
            {
                _turnTime += Time.unscaledDeltaTime * _turnSpeed / _turnRange;
                _anchor.localRotation = Quaternion.Euler(0f, Mathf.Sin(_turnTime) * _turnRange, 0f);
            }

            PoseGun(Time.unscaledDeltaTime);
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
