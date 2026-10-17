using System;

namespace Purge.Core.Feel
{
    /// <summary>A channel of the shooting feel (ТЗ «Ощущение стрельбы»): one kind of response to a shot, hit or kill.</summary>
    public enum FeelChannel
    {
        Knockback = 0,
        Stagger = 1,
        KillFling = 2,
        Recoil = 3,
        CameraKick = 4,
        CameraShake = 5,
        GunKick = 6,
        BodyKick = 7,
        ShotLight = 8,
        HitFlash = 9,
        Dent = 10,
        Splash = 11,
        HitRing = 12,
        Vibration = 13,
        HitSound = 14,
        KillSound = 15,
    }

    /// <summary>
    /// Multipliers of the feel channels (1 = as the weapon's <c>FeelModule</c> says, 0 = off): the dev stand turns
    /// channels on and off and scales them while playing to find the right amount. Application lifetime (kept between
    /// levels of a session), not saved. Plain data; gameplay and views multiply by it.
    /// </summary>
    public sealed class FeelChannels
    {
        public const float MaxScale = 3f;

        public static readonly int Count = Enum.GetValues(typeof(FeelChannel)).Length;

        private readonly float[] _scale = new float[Count];
        private readonly bool[] _off = new bool[Count];

        public FeelChannels() => Reset();

        /// <summary>Effective multiplier: 0 when the channel is off.</summary>
        public float this[FeelChannel channel] => _off[(int)channel] ? 0f : _scale[(int)channel];

        public float Scale(FeelChannel channel) => _scale[(int)channel];

        public bool IsOn(FeelChannel channel) => !_off[(int)channel];

        public void SetScale(FeelChannel channel, float scale) =>
            _scale[(int)channel] = Math.Max(0f, Math.Min(MaxScale, scale));

        public void SetOn(FeelChannel channel, bool on) => _off[(int)channel] = !on;

        public void Reset()
        {
            for (var i = 0; i < Count; i++)
            {
                _scale[i] = 1f;
                _off[i] = false;
            }
        }
    }
}
