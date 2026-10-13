using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Audio;
using Maze.Core.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maze.Application.Services
{
    /// <summary>Who a sound belongs to: level sounds pause with the gameplay and stop when the level ends.</summary>
    public enum SoundChannel
    {
        Interface = 0,
        Level = 1,
    }

    /// <summary>
    /// Music and sounds (ТЗ §71). Loads the <see cref="AudioCatalog"/> once (resident); music clips are loaded only
    /// while they play, through their own asset owner. Sounds are 2D (top-down: views give volume and pan from the
    /// distance to the player, <see cref="SoundMath"/>) and play on a fixed pool of voices: a cue plays at most
    /// <see cref="SoundCue.MaxVoices"/> copies (a new one stops its oldest), a full pool stops the oldest one-shot.
    /// Volumes come from <see cref="SettingsService"/> and apply to playing sounds too. Ticked by the composition root
    /// with unscaled time (fades, music loops), so it works while the game is paused.
    /// </summary>
    public sealed class AudioService : IApplicationService, IDisposable
    {
        private const int VoiceCount = 24;

        private readonly IAddressablesService _addressables;
        private readonly SettingsService _settings;
        private readonly Voice[] _voices = new Voice[VoiceCount];
        private readonly Dictionary<SoundCue, CueState> _cues = new Dictionary<SoundCue, CueState>();
        private readonly MusicSlot[] _music = new MusicSlot[2];
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly DeterministicRandom _random = new DeterministicRandom(Environment.TickCount);

        private IAssetOwner _assets;
        private GameObject _root;
        private int _nextId;
        private bool _levelPaused;
        private float _musicShare = 1f;
        private float _musicShareTarget = 1f;
        private int _musicRequest;
        private int _musicLoading;
        private bool _disposed;

        public AudioService(IAddressablesService addressables, SettingsService settings)
        {
            _addressables = addressables;
            _settings = settings;
        }

        public string Name => "Audio";

        /// <summary>Null until initialized.</summary>
        public AudioCatalog Catalog { get; private set; }

        /// <summary>The track that plays or is being loaded (null = silence).</summary>
        public MusicTrack CurrentMusic => _music[0].Track;

        /// <summary>
        /// Addressables handles held by music right now (playing, fading out, loading) — one per track. Music comes and
        /// goes with the flow, so leak checks leave these out.
        /// </summary>
        public int MusicHandleCount
        {
            get
            {
                var count = _musicLoading;
                foreach (var slot in _music)
                    if (slot?.Owner != null)
                        count++;
                return count;
            }
        }

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            _assets = _addressables.CreateOwner("Application: Audio");
            Catalog = await _assets.LoadAsync<AudioCatalog>(AudioCatalog.Address, cancellation);

            // In the Bootstrap scene (active while the application starts): it lives as long as the application.
            _root = new GameObject("Audio");
            for (var i = 0; i < _voices.Length; i++)
                _voices[i] = new Voice { Source = CreateSource("Voice " + i) };
            for (var i = 0; i < _music.Length; i++)
                _music[i] = new MusicSlot { A = CreateSource("Music " + i + "A"), B = CreateSource("Music " + i + "B") };

            _settings.VolumeChanged += ApplyVolumes;
        }

        // ------------------------------------------------------------------ Sounds

        /// <summary>
        /// Plays a variant of the cue once. <paramref name="volume"/> scales the cue's volume (distance), pan is -1..1,
        /// <paramref name="pitch"/> scales its random pitch. Returns a voice id for <see cref="Stop"/>, 0 when silent.
        /// </summary>
        public int Play(SoundCue cue, SoundChannel channel, float volume = 1f, float pan = 0f, float pitch = 1f) =>
            Start(cue, channel, volume, pan, pitch, false, 0f);

        /// <summary>
        /// Starts the cue looping (a level ambience). <paramref name="startShare"/> 0..1 = where in the clip it begins
        /// (several loops of one clip should not play in step). Change it with <see cref="SetVoice"/>.
        /// </summary>
        public int PlayLoop(SoundCue cue, SoundChannel channel, float volume, float pan, float startShare) =>
            Start(cue, channel, volume, pan, 1f, true, startShare);

        /// <summary>New volume (scale of the cue's volume) and pan of a playing voice.</summary>
        public void SetVoice(int id, float volume, float pan)
        {
            if (_root == null) return;
            var voice = Find(id);
            if (voice == null) return;
            voice.Volume = volume;
            voice.Source.panStereo = pan;
            voice.Source.volume = VoiceVolume(voice);
        }

        public bool IsPlaying(int id) => Find(id) != null;

        /// <summary>Voices of the channel playing or paused now (checks, tests).</summary>
        public int PlayingCount(SoundChannel channel)
        {
            var count = 0;
            foreach (var voice in _voices)
                if (voice != null && voice.Id != 0 && voice.Channel == channel)
                    count++;
            return count;
        }

        public void Stop(int id)
        {
            if (_root == null) return; // Destroyed with the scene (quit): nothing plays any more.
            var voice = Find(id);
            if (voice != null) Release(voice);
        }

        /// <summary>Pauses or resumes every level sound (the gameplay is paused: pause, map, "Finish level?").</summary>
        public void SetLevelPaused(bool paused)
        {
            if (_levelPaused == paused || _root == null) return;
            _levelPaused = paused;
            foreach (var voice in _voices)
            {
                if (voice == null || voice.Id == 0 || voice.Channel != SoundChannel.Level) continue;
                voice.Paused = paused;
                if (paused) voice.Source.Pause();
                else voice.Source.UnPause();
            }
        }

        /// <summary>
        /// Stops level sounds: all of them, or only the loops (the level ended: its last sounds — a death, an exit —
        /// may finish).
        /// </summary>
        public void StopLevelSounds(bool loopsOnly = false)
        {
            if (_root == null) return;
            foreach (var voice in _voices)
                if (voice != null && voice.Id != 0 && voice.Channel == SoundChannel.Level && (!loopsOnly || voice.Loop))
                    Release(voice);
            if (!loopsOnly) SetLevelPaused(false);
        }

        // ------------------------------------------------------------------ Music

        /// <summary>
        /// Cross-fades to <paramref name="track"/> (null or unset = fade to silence). The same track keeps playing.
        /// </summary>
        public void PlayMusic(MusicTrack track)
        {
            if (_root == null) return;
            if (track != null && !track.IsSet) track = null;
            if (ReferenceEquals(_music[0].Track, track)) return;

            // The playing track becomes the fading one; a track still fading out is cut.
            _music[1].Clear();
            (_music[0], _music[1]) = (_music[1], _music[0]);
            _music[1].FadeTarget = 0f;
            _music[0].Track = track;
            _music[0].Gain = 0f;
            _music[0].FadeTarget = 1f;
            var request = ++_musicRequest;
            if (track != null)
                LoadMusicAsync(track, request).Forget();
        }

        /// <summary>Share of the music volume (pause, result jingle); reached smoothly.</summary>
        public void SetMusicShare(float share) => _musicShareTarget = Mathf.Clamp01(share);

        /// <summary>Fades, music loops, finished voices. Unscaled time.</summary>
        public void Tick(float deltaTime)
        {
            if (_root == null) return;

            foreach (var voice in _voices)
                if (voice.Id != 0 && !voice.Loop && !voice.Source.isPlaying && !voice.Paused)
                    Release(voice);

            var fade = Catalog != null && Catalog.MusicFade > 0f ? deltaTime / Catalog.MusicFade : 1f;
            _musicShare = Mathf.MoveTowards(_musicShare, _musicShareTarget, deltaTime * 2f);
            for (var i = 0; i < _music.Length; i++)
            {
                var slot = _music[i];
                // A track still loading fades in from its first sound, not from when it was asked for.
                if (i > 0 || slot.Owner != null || slot.Track == null)
                    slot.Gain = Mathf.MoveTowards(slot.Gain, slot.FadeTarget, fade);
                if (i > 0 && slot.Gain <= 0f && slot.Owner != null)
                {
                    slot.Clear();
                    continue;
                }

                slot.Loop();
                slot.SetVolume(_settings.MusicVolume * _musicShare);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _settings.VolumeChanged -= ApplyVolumes;
            _lifetime.Cancel();
            _lifetime.Dispose();
            // On application quit or leaving Play Mode the Bootstrap scene (with the voices) is destroyed before the
            // scope disposes this service: Unity objects are checked, the asset handles are released anyway.
            foreach (var slot in _music)
                slot?.Clear();
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _assets?.Dispose();
            _assets = null;
            Catalog = null;
            _cues.Clear();
        }

        // ------------------------------------------------------------------ Internals

        private int Start(SoundCue cue, SoundChannel channel, float volume, float pan, float pitch, bool loop, float startShare)
        {
            if (_root == null || cue == null || cue.IsEmpty || volume <= 0f)
                return 0;

            var now = Time.unscaledTime;
            if (!_cues.TryGetValue(cue, out var state))
                _cues[cue] = state = new CueState { LastTime = float.NegativeInfinity, LastClip = -1 };
            if (!loop && cue.MinInterval > 0f && now - state.LastTime < cue.MinInterval)
                return 0;

            var clips = cue.Clips;
            var index = SoundMath.PickVariant(clips.Length, state.LastClip, _random.NextDouble());
            var clip = clips[index] ?? FirstClip(clips);
            if (clip == null) return 0;

            var voice = TakeVoice(cue, loop);
            if (voice == null) return 0;

            state.LastTime = now;
            state.LastClip = index;
            voice.Id = ++_nextId == 0 ? ++_nextId : _nextId;
            voice.Cue = cue;
            voice.Channel = channel;
            voice.Volume = volume;
            voice.Loop = loop;
            voice.Started = now;
            voice.Paused = channel == SoundChannel.Level && _levelPaused;

            var source = voice.Source;
            source.clip = clip;
            source.loop = loop;
            source.panStereo = Mathf.Clamp(pan, -1f, 1f);
            source.pitch = Mathf.Lerp(cue.PitchMin, cue.PitchMax, (float)_random.NextDouble()) * pitch;
            source.volume = VoiceVolume(voice);
            source.time = loop ? Mathf.Repeat(startShare, 1f) * clip.length : 0f;
            source.Play();
            if (voice.Paused) source.Pause();
            return voice.Id;
        }

        /// <summary>The cue's oldest voice when it is at its limit; else a free one; else the oldest one-shot.</summary>
        private Voice TakeVoice(SoundCue cue, bool loop)
        {
            Voice oldestOfCue = null, free = null, oldest = null;
            var ofCue = 0;
            foreach (var voice in _voices)
            {
                if (voice.Id == 0)
                {
                    free ??= voice;
                    continue;
                }

                if (voice.Cue == cue && voice.Loop == loop)
                {
                    ofCue++;
                    if (oldestOfCue == null || voice.Started < oldestOfCue.Started) oldestOfCue = voice;
                }

                if (!voice.Loop && (oldest == null || voice.Started < oldest.Started)) oldest = voice;
            }

            var taken = ofCue >= cue.MaxVoices ? oldestOfCue : free ?? oldest;
            if (taken != null && taken.Id != 0) Release(taken);
            return taken;
        }

        private Voice Find(int id)
        {
            if (id == 0) return null;
            foreach (var voice in _voices)
                if (voice != null && voice.Id == id)
                    return voice;
            return null;
        }

        private void Release(Voice voice)
        {
            voice.Id = 0;
            voice.Cue = null;
            voice.Paused = false;
            if (voice.Source == null) return;
            voice.Source.Stop();
            voice.Source.clip = null;
        }

        private float VoiceVolume(Voice voice) => voice.Cue.Volume * voice.Volume * _settings.SfxVolume;

        private void ApplyVolumes()
        {
            if (_root == null) return;
            foreach (var voice in _voices)
                if (voice != null && voice.Id != 0)
                    voice.Source.volume = VoiceVolume(voice);
            // Music follows in the next Tick.
        }

        private async UniTaskVoid LoadMusicAsync(MusicTrack track, int request)
        {
            var owner = _addressables.CreateOwner("Application: Music");
            AudioClip clip;
            _musicLoading++;
            try
            {
                clip = await owner.LoadAsync<AudioClip>(track.Clip, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                owner.Dispose();
                return;
            }
            catch (Exception e)
            {
                owner.Dispose();
                GameLog.Exception(LogChannel.Addressables, e, "Loading music failed");
                return;
            }
            finally
            {
                _musicLoading--;
            }

            var slot = _music[0];
            if (_disposed || request != _musicRequest || !ReferenceEquals(slot.Track, track))
            {
                owner.Dispose(); // Another track was asked for meanwhile.
                return;
            }

            slot.Start(owner, clip);
        }

        private AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 128;
            return source;
        }

        private static AudioClip FirstClip(AudioClip[] clips)
        {
            foreach (var clip in clips)
                if (clip != null)
                    return clip;
            return null;
        }

        private sealed class Voice
        {
            public AudioSource Source;
            public int Id;
            public SoundCue Cue;
            public SoundChannel Channel;
            public float Volume;
            public bool Loop;
            public bool Paused;
            public float Started;
        }

        private sealed class CueState
        {
            public float LastTime;
            public int LastClip;
        }

        /// <summary>
        /// A music track on two sources: with a loop overlap the next pass starts on the other source while the
        /// previous one plays its fading tail.
        /// </summary>
        private sealed class MusicSlot
        {
            public AudioSource A;
            public AudioSource B;
            public MusicTrack Track;
            public IAssetOwner Owner;
            public float Gain;
            public float FadeTarget;
            private AudioSource _active;

            public void Start(IAssetOwner owner, AudioClip clip)
            {
                Owner?.Dispose();
                Owner = owner;
                var overlap = Track.LoopOverlap > 0f && Track.LoopOverlap < clip.length * 0.5f;
                foreach (var source in new[] { A, B })
                {
                    source.Stop();
                    source.clip = clip;
                    source.loop = !overlap;
                }

                _active = A;
                A.Play();
            }

            public void Loop()
            {
                if (_active == null || _active.loop || _active.clip == null) return;
                if (_active.time < _active.clip.length - Track.LoopOverlap && _active.isPlaying) return;

                _active = _active == A ? B : A;
                _active.time = 0f;
                _active.Play();
            }

            public void SetVolume(float musicVolume)
            {
                var volume = Track != null ? Track.Volume * Gain * musicVolume : 0f;
                A.volume = volume;
                B.volume = volume;
            }

            /// <summary>Stops the sources (if they still exist: on quit the scene may be destroyed first) and releases the clip.</summary>
            public void Clear()
            {
                foreach (var source in new[] { A, B })
                {
                    if (source == null) continue;
                    source.Stop();
                    source.clip = null;
                }

                _active = null;
                Owner?.Dispose();
                Owner = null;
                Track = null;
                Gain = 0f;
                FadeTarget = 0f;
            }
        }
    }
}
