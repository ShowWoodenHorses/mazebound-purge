using System;
using UnityEngine;

namespace Maze.Core.Audio
{
    /// <summary>
    /// One sound of the game: variants (a random one each time, never the same twice in a row), volume, pitch spread,
    /// how far it is heard and how many copies may play at once. An empty cue is simply silent.
    /// </summary>
    [Serializable]
    public sealed class SoundCue
    {
        [Tooltip("Variants; a random one plays each time.")]
        [SerializeField] private AudioClip[] _clips = Array.Empty<AudioClip>();

        [Tooltip("Volume of the clips (before the player's sound volume). 1 is the most: a recording too quiet at 1 " +
                 "must be made louder in the file.")]
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;

        [Tooltip("Random pitch in [min, max] every time (variety for frequent sounds).")]
        [SerializeField] private Vector2 _pitch = new Vector2(0.95f, 1.05f);

        [Tooltip("Sounds in the level: heard up to this many cells from the player (full volume near, silent at the " +
                 "range). 0 = everywhere at full volume (interface, music stingers).")]
        [SerializeField, Min(0f)] private float _range = 10f;

        [Tooltip("At most this many copies at once; a new one stops the oldest (rapid fire, many zombies).")]
        [SerializeField, Min(1)] private int _maxVoices = 3;

        [Tooltip("Seconds after a play during which the cue is not played again (0 = no limit).")]
        [SerializeField, Min(0f)] private float _minInterval;

        public SoundCue()
        {
        }

        public SoundCue(float volume, float range, int maxVoices, Vector2 pitch, float minInterval = 0f)
        {
            _volume = volume;
            _range = range;
            _maxVoices = maxVoices;
            _pitch = pitch;
            _minInterval = minInterval;
        }

        /// <summary>A cue with the same tuning and no clips (a new sound starts from an existing one's settings).</summary>
        internal SoundCue CopyTuning() => new SoundCue(_volume, _range, _maxVoices, _pitch, _minInterval);

        public AudioClip[] Clips { get => _clips; internal set => _clips = value ?? Array.Empty<AudioClip>(); }
        public float Volume { get => _volume; internal set => _volume = value; }
        public float PitchMin => Mathf.Min(_pitch.x, _pitch.y);
        public float PitchMax => Mathf.Max(_pitch.x, _pitch.y);
        public float Range { get => _range; internal set => _range = value; }
        public int MaxVoices => Mathf.Max(1, _maxVoices);
        public float MinInterval => _minInterval;

        public bool IsEmpty
        {
            get
            {
                if (_clips == null) return true;
                foreach (var clip in _clips)
                    if (clip != null)
                        return false;
                return true;
            }
        }

        /// <summary>Volume factor of a sound <paramref name="distance"/> cells away: 1 near, 0 at <see cref="Range"/>.</summary>
        public float Attenuation(float distance) => SoundMath.Attenuation(distance, _range);
    }

    /// <summary>Pure helpers of the positional sound model (top-down: no Unity 3D audio, the listener is the player).</summary>
    public static class SoundMath
    {
        /// <summary>Full volume within this share of the range.</summary>
        public const float NearShare = 0.15f;

        /// <summary>Cells to the side at which a sound is panned fully (to <see cref="MaxPan"/>).</summary>
        public const float PanDistance = 8f;

        /// <summary>Strongest stereo pan; a sound is never only in one ear.</summary>
        public const float MaxPan = 0.6f;

        /// <summary>
        /// 1 within <see cref="NearShare"/> of <paramref name="range"/>, falling smoothly to 0 at the range;
        /// a range of 0 or less means "everywhere" (1).
        /// </summary>
        public static float Attenuation(float distance, float range)
        {
            if (range <= 0f) return 1f;
            var near = range * NearShare;
            if (distance <= near) return 1f;
            if (distance >= range) return 0f;
            var t = 1f - (distance - near) / (range - near);
            return t * t; // Quieter fast at first, like a real fall-off, and reaches 0 softly.
        }

        /// <summary>Stereo pan of a sound <paramref name="offsetX"/> cells to the East of the listener.</summary>
        public static float Pan(float offsetX) => Mathf.Clamp(offsetX / PanDistance, -1f, 1f) * MaxPan;

        /// <summary>
        /// A random index in [0, count) that differs from <paramref name="previous"/> when there is a choice.
        /// <paramref name="random01"/> is uniform in [0, 1).
        /// </summary>
        public static int PickVariant(int count, int previous, double random01)
        {
            if (count <= 1) return 0;
            if (previous < 0 || previous >= count)
                return Math.Min(count - 1, (int)(random01 * count));

            var index = Math.Min(count - 2, (int)(random01 * (count - 1)));
            return index >= previous ? index + 1 : index;
        }
    }
}
