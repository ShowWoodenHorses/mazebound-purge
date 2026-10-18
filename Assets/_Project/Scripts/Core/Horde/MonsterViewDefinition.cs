using System;
using UnityEngine;

namespace Purge.Core.Horde
{
    /// <summary>When a cast clip lets go (share of it) and its length at 1×, seconds (measured by Build Monsters).</summary>
    [Serializable]
    public struct CastClipTiming
    {
        [Range(0.05f, 1f)] public float Moment;
        [Min(0f)] public float Length;

        public CastClipTiming(float moment, float length)
        {
            Moment = moment;
            Length = length;
        }
    }

    /// <summary>
    /// Look of an officer, elite or boss (ТЗ: Animator, not baked): a prefab with a Humanoid Animator whose controller
    /// Build Monsters makes, the equipment in sockets on the hand bones (built into the prefab), the measured ground
    /// speeds of its walk and run and the moment its cast clip lets go. Kept apart from the
    /// <see cref="Monsters.MonsterDefinition"/> (by <see cref="MonsterId"/>): the content catalog loads every definition
    /// and must stay light. Also how the monster's projectiles look.
    /// </summary>
    [CreateAssetMenu(menuName = "Purge/Monster View", fileName = "MonsterView")]
    public sealed class MonsterViewDefinition : ScriptableObject
    {
        [Tooltip("Whose look it is.")]
        [SerializeField, Content.ContentRef(typeof(Monsters.MonsterDefinition))] private string _monsterId;
        [Tooltip("The model with its Animator (Build Monsters sets the controller, scale and sockets up).")]
        [SerializeField] private GameObject _prefab;
        [Tooltip("Models put on bones of the prefab (Build Monsters reads it; rebuild after a change).")]
        [SerializeField] private HordeEquipment[] _equipment = Array.Empty<HordeEquipment>();
        [Tooltip("Ground speed of the run clip at 1×, cells per second (measured; 0 = play at 1×).")]
        [SerializeField, Min(0f)] private float _runSpeed;
        [Tooltip("Ground speed of the walk clip at 1×, cells per second (measured; 0 = play at 1×).")]
        [SerializeField, Min(0f)] private float _walkSpeed;
        [Tooltip("Share of the cast clip at which it lets go (the clip is fitted so this falls at the end of the warning).")]
        [SerializeField, Range(0.05f, 1f)] private float _castMoment = 0.6f;
        [Tooltip("Length of the cast clip at 1×, seconds.")]
        [SerializeField, Min(0.05f)] private float _castLength = 1f;
        [Tooltip("Casts of the second, third… ability when they have their own clips (stage 5d, the Brute); empty = as the first.")]
        [SerializeField] private CastClipTiming[] _moreCasts = Array.Empty<CastClipTiming>();
        [Tooltip("Colour of the splashes when it is hit or killed, sRGB.")]
        [SerializeField] private Color _splashColor = new Color(0.55f, 0.85f, 0.25f, 1f);

        [Header("Projectiles")]
        [Tooltip("Model of its projectile (pointing +Z); empty = a glowing blob of the colour below.")]
        [SerializeField] private GameObject _projectileModel;
        [SerializeField] private Color _projectileColor = new Color(0.6f, 1f, 0.3f, 1f);
        [Tooltip("Size of the projectile, cells.")]
        [SerializeField, Min(0.05f)] private float _projectileSize = 0.35f;

        public string MonsterId => _monsterId;
        public GameObject Prefab => _prefab;
        public HordeEquipment[] Equipment => _equipment ??= Array.Empty<HordeEquipment>();
        public float RunSpeed => _runSpeed;
        public float WalkSpeed => _walkSpeed;
        public float CastMoment => _castMoment;
        public float CastLength => _castLength;
        public Color SplashColor => _splashColor;
        public GameObject ProjectileModel => _projectileModel;
        public Color ProjectileColor => _projectileColor;
        public float ProjectileSize => _projectileSize;

        /// <summary>When the cast clip of ability <paramref name="index"/> lets go (share) and how long it is at 1×, s.</summary>
        public void CastTiming(int index, out float moment, out float length)
        {
            if (index > 0 && _moreCasts != null && index - 1 < _moreCasts.Length && _moreCasts[index - 1].Length > 0f)
            {
                moment = _moreCasts[index - 1].Moment;
                length = _moreCasts[index - 1].Length;
                return;
            }

            moment = _castMoment;
            length = _castLength;
        }

        internal void Configure(string monsterId, GameObject prefab, float runSpeed, float walkSpeed, float castMoment,
            float castLength, params CastClipTiming[] moreCasts)
        {
            _monsterId = monsterId;
            _prefab = prefab;
            _runSpeed = runSpeed;
            _walkSpeed = walkSpeed;
            _castMoment = castMoment;
            _castLength = castLength;
            _moreCasts = moreCasts ?? Array.Empty<CastClipTiming>();
        }

        internal void ConfigureLook(HordeEquipment[] equipment, Color splash, GameObject projectileModel, Color projectileColor,
            float projectileSize)
        {
            _equipment = equipment ?? Array.Empty<HordeEquipment>();
            _splashColor = splash;
            _projectileModel = projectileModel;
            _projectileColor = projectileColor;
            _projectileSize = projectileSize;
        }
    }
}
