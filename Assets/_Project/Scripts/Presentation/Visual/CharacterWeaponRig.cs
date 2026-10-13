using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Where a character holds weapons (display only, set up by Maze → Dev → Build Weapons from the animation poses):
    /// <list type="bullet">
    /// <item><see cref="MeleeSocket"/> — child of the right hand; a melee weapon's grip pivot goes here, blade along
    /// its +Z (the sword clips grip consistently, so the weapon stays rigid in the hand);</item>
    /// <item><see cref="PalmRight"/> / <see cref="PalmLeft"/> — palm centres. A two-handed gun's grip sits in the right
    /// palm, its barrel points where the clip's hands point it (right palm → left palm).</item>
    /// </list>
    /// <see cref="PoseGun"/> runs after the Animator every frame while a gun is held:
    /// <list type="number">
    /// <item>the rifle clips stand bladed (chest 40–55° to the right of where the character faces): the spine is turned
    /// toward the facing down to <see cref="_bodyYaw"/>, at most <see cref="_maxTwist"/>, legs untouched;</item>
    /// <item>the gun and the right hand keep the clip's direction (the barrel still points where the clip aims), then the
    /// right wrist turns the barrel toward the facing by <see cref="_aimBarrel"/> (the rifle clips point it ~23° left in
    /// the stance, ~7° left at best in the shot);</item>
    /// <item>the left hand is pulled onto the gun's <see cref="WeaponModel.GripLeft"/> (<see cref="TwoBoneIK"/>); if the
    /// grip is out of the arm's reach, the gun is turned about the right palm just enough (<see cref="BringIntoReach"/>); except
    /// in states tagged <see cref="PlayerAnimatorParameters.NoHandIKTag"/> (reload, hit, use).</item>
    /// </list>
    /// All of it fades in and out over <see cref="_blendTime"/>, and is off in states tagged
    /// <see cref="PlayerAnimatorParameters.NoWeaponPoseTag"/> (death).
    /// </summary>
    public sealed class CharacterWeaponRig : MonoBehaviour
    {
        private static readonly float[] SpineShares = { 0.3f, 0.35f, 0.35f }; // Spine, Chest, UpperChest
        private const float ReachMargin = 0.98f; // a fully straight arm looks locked

        [SerializeField] private Transform _meleeSocket;
        [SerializeField] private Transform _palmRight;
        [SerializeField] private Transform _palmLeft;

        [Tooltip("With a gun, the chest is turned toward the facing until it is this many degrees off it.")]
        [SerializeField, Range(0f, 60f)] private float _bodyYaw = 20f;

        [Tooltip("Largest spine turn with a gun, degrees (the legs stay as in the clip).")]
        [SerializeField, Range(0f, 60f)] private float _maxTwist = 40f;

        [Tooltip("Turns a gun's barrel toward the facing (the line of fire) by the wrist, around the vertical: 0 = as in the " +
                 "clip, 1 = exactly along the facing. The left hand follows the gun.")]
        [SerializeField, Range(0f, 1f)] private float _aimBarrel;

        [Tooltip("Seconds to blend the gun pose and the left hand IK in or out.")]
        [SerializeField, Min(0f)] private float _blendTime = 0.15f;

        private readonly Transform[] _spine = new Transform[3];
        private Animator _animator;
        private Transform _leftShoulder;
        private Transform _rightShoulder;
        private Transform _leftUpperArm;
        private Transform _leftLowerArm;
        private Transform _leftHand;
        private Transform _rightHand;
        private bool _bonesResolved;
        private bool _bonesValid;
        private float _poseWeight;
        private float _handWeight;

        public Transform MeleeSocket => _meleeSocket;
        public Transform PalmRight => _palmRight;
        public Transform PalmLeft => _palmLeft;

        public bool IsValid => _meleeSocket != null && _palmRight != null && _palmLeft != null;

        /// <summary>Largest spine turn with a gun, degrees; 0 = the chest stays as in the clip (for tests).</summary>
        public float MaxTwist => _maxTwist;

        /// <summary>How far the barrel is turned from the clip's direction to the facing, 0..1 (for tests).</summary>
        public float AimBarrelWeight => _aimBarrel;

        /// <summary>Current blend of the left hand IK, 0..1 (for tests).</summary>
        public float HandWeight => _handWeight;

        /// <summary>World rotation of a two-handed gun now: barrel from the right palm to the left one, kept upright.</summary>
        public Quaternion TwoHandedRotation()
        {
            var forward = _palmLeft.position - _palmRight.position;
            if (forward.sqrMagnitude < 1e-6f)
                return transform.rotation;
            var up = Vector3.ProjectOnPlane(Vector3.up, forward);
            return Quaternion.LookRotation(forward, up.sqrMagnitude > 1e-6f ? up : transform.up);
        }

        /// <summary>
        /// Call every frame after the Animator, with the gun in hands (a child of <see cref="PalmRight"/> at its pivot)
        /// or null when no gun is held, and the gun's left-hand mark (null: the left hand is not moved).
        /// </summary>
        public void PoseGun(Transform gun, Transform gripLeft, float deltaTime)
        {
            ResolveBones();
            var posing = gun != null && !InState(PlayerAnimatorParameters.NoWeaponPoseTag);
            var holding = posing && gripLeft != null && _bonesValid && !InState(PlayerAnimatorParameters.NoHandIKTag);
            var step = _blendTime > 0f ? deltaTime / _blendTime : 1f;
            _poseWeight = Mathf.MoveTowards(_poseWeight, posing ? 1f : 0f, step);
            _handWeight = Mathf.MoveTowards(_handWeight, holding ? 1f : 0f, step);

            if (!_bonesValid)
            {
                if (gun != null) gun.rotation = TwoHandedRotation();
                return;
            }

            // The clip's gun direction and right hand, before the spine moves the arms.
            var clipGun = TwoHandedRotation();
            var clipRightHand = _rightHand.rotation;

            if (_poseWeight > 0f)
                TurnSpine(_poseWeight);
            if (gun == null) return;

            _rightHand.rotation = Quaternion.Slerp(_rightHand.rotation, clipRightHand, _poseWeight);
            gun.rotation = Quaternion.Slerp(TwoHandedRotation(), clipGun, _poseWeight);
            if (_aimBarrel > 0f)
                AimBarrel(gun, _aimBarrel * _poseWeight);

            if (_handWeight <= 0f) return;
            // Hand rotation that puts the palm on the mark, and where the hand must be for that.
            var handRotation = gripLeft.rotation * Quaternion.Inverse(_palmLeft.localRotation);
            var palmOffset = handRotation * (Quaternion.Inverse(_leftHand.rotation) * (_palmLeft.position - _leftHand.position));
            var handTarget = gripLeft.position - palmOffset;
            var turn = BringIntoReach(gun.position, handTarget);
            if (turn != Quaternion.identity)
            {
                gun.rotation = turn * gun.rotation;
                handTarget = gun.position + turn * (handTarget - gun.position);
                handRotation = turn * handRotation;
            }

            TwoBoneIK.Solve(_leftUpperArm, _leftLowerArm, _leftHand, handTarget, _handWeight);
            _leftHand.rotation = Quaternion.Slerp(_leftHand.rotation, handRotation, _handWeight);
        }

        /// <summary>
        /// Some clips (rifle idle) hold the gun lower and farther from the left shoulder than the shooting pose, and
        /// turning the chest moves that shoulder back: the grip may be out of the left arm's reach. Then the gun is
        /// turned about its pivot (the right palm) toward the shoulder just enough — the right hand keeps the grip, the
        /// barrel moves a little. Identity when the grip is within reach.
        /// </summary>
        private Quaternion BringIntoReach(Vector3 pivot, Vector3 handTarget)
        {
            var shoulder = _leftUpperArm.position;
            var reach = ((_leftLowerArm.position - shoulder).magnitude + (_leftHand.position - _leftLowerArm.position).magnitude) *
                        ReachMargin;
            if ((handTarget - shoulder).sqrMagnitude <= reach * reach) return Quaternion.identity;

            var toTarget = handTarget - pivot;
            var toShoulder = shoulder - pivot;
            var radius = toTarget.magnitude;
            var distance = toShoulder.magnitude;
            var axis = Vector3.Cross(toTarget, toShoulder);
            if (radius < 1e-4f || distance < 1e-4f || axis.sqrMagnitude < 1e-8f) return Quaternion.identity;

            // Angle between pivot → target and pivot → shoulder at which the target is exactly at reach.
            var wanted = Mathf.Acos(Mathf.Clamp((distance * distance + radius * radius - reach * reach) / (2f * distance * radius),
                -1f, 1f)) * Mathf.Rad2Deg;
            var current = Vector3.Angle(toTarget, toShoulder);
            return current > wanted
                ? Quaternion.AngleAxis((current - wanted) * _handWeight, axis.normalized)
                : Quaternion.identity;
        }

        /// <summary>
        /// Turns the right wrist around the vertical so that the barrel (the gun's +Z) points <paramref name="weight"/> of
        /// the way to the facing; the gun, a child of the right palm, turns with it.
        /// </summary>
        private void AimBarrel(Transform gun, float weight)
        {
            var up = transform.up;
            var barrel = Vector3.ProjectOnPlane(gun.forward, up);
            if (barrel.sqrMagnitude < 1e-6f) return;

            var yaw = Vector3.SignedAngle(barrel, transform.forward, up) * weight;
            if (Mathf.Abs(yaw) < 0.01f) return;

            var gunRotation = gun.rotation;
            var turn = Quaternion.AngleAxis(yaw, up);
            _rightHand.rotation = turn * _rightHand.rotation;
            gun.rotation = turn * gunRotation; // Exact even if the gun is not a child of the hand.
        }

        /// <summary>Turns the chest toward the facing by the part of its yaw beyond <see cref="_bodyYaw"/>.</summary>
        private void TurnSpine(float weight)
        {
            var up = transform.up;
            var across = Vector3.ProjectOnPlane(_rightShoulder.position - _leftShoulder.position, up);
            if (across.sqrMagnitude < 1e-6f) return;

            var yaw = Vector3.SignedAngle(transform.forward, Vector3.Cross(across, up), up);
            var excess = yaw > _bodyYaw ? Mathf.Min(yaw - _bodyYaw, _maxTwist)
                : yaw < -_bodyYaw ? Mathf.Max(yaw + _bodyYaw, -_maxTwist)
                : 0f;
            if (excess == 0f) return;

            var total = 0f;
            for (var i = 0; i < _spine.Length; i++)
                if (_spine[i] != null) total += SpineShares[i];
            for (var i = 0; i < _spine.Length; i++)
                if (_spine[i] != null)
                    _spine[i].rotation = Quaternion.AngleAxis(-excess * weight * SpineShares[i] / total, up) * _spine[i].rotation;
        }

        private bool InState(int tag)
        {
            if (_animator == null || !_animator.isActiveAndEnabled) return false;
            for (var layer = 0; layer < _animator.layerCount; layer++)
            {
                if (_animator.GetCurrentAnimatorStateInfo(layer).tagHash == tag) return true;
                if (_animator.IsInTransition(layer) && _animator.GetNextAnimatorStateInfo(layer).tagHash == tag) return true;
            }

            return false;
        }

        private void ResolveBones()
        {
            if (_bonesResolved) return;
            _bonesResolved = true;
            _animator = GetComponent<Animator>();
            if (_animator == null || !_animator.isHuman) return;

            _spine[0] = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _spine[1] = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _spine[2] = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _leftShoulder = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rightShoulder = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _leftUpperArm = _leftShoulder;
            _leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _bonesValid = IsValid && _leftShoulder != null && _rightShoulder != null && _leftLowerArm != null &&
                          _leftHand != null && _rightHand != null && (_spine[0] != null || _spine[1] != null);
        }
    }
}
