using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Purge.Application.Platform
{
    /// <summary>
    /// Short vibrations of the shooting feel: the phone (web — <c>navigator.vibrate</c>, Android — the system vibrator)
    /// and the gamepad's motors. A new vibration within <see cref="MinGap"/> of the last one is skipped (an automatic
    /// weapon does not buzz without a break). <see cref="Enabled"/> — the player's choice (on by default).
    /// <see cref="Tick"/> stops the gamepad motors when the time is up: call it every frame from a view.
    /// </summary>
    public sealed class VibrationService
    {
        private const float MinGap = 0.06f;
        private const int MaxMilliseconds = 100;

        private float _last = float.NegativeInfinity;
        private float _padStopAt = -1f;
        private Gamepad _pad;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _vibrator;
        private bool _vibratorResolved;
#endif

        public bool Enabled { get; set; } = true;

        /// <summary>Vibrates for <paramref name="milliseconds"/> (clamped to 100); <paramref name="strength"/> 0..1 — gamepad motors.</summary>
        public void Vibrate(int milliseconds, float strength = 0.5f)
        {
            if (!Enabled || milliseconds <= 0) return;
            var now = Time.unscaledTime;
            if (now - _last < MinGap) return;
            _last = now;
            milliseconds = Mathf.Min(milliseconds, MaxMilliseconds);

            var pad = Gamepad.current;
            if (pad != null)
            {
                var low = Mathf.Clamp01(strength);
                pad.SetMotorSpeeds(low, Mathf.Clamp01(strength * 0.6f));
                _pad = pad;
                _padStopAt = now + milliseconds / 1000f;
            }

            Phone(milliseconds);
        }

        public void Tick()
        {
            if (_padStopAt < 0f || Time.unscaledTime < _padStopAt) return;
            _padStopAt = -1f;
            _pad?.SetMotorSpeeds(0f, 0f);
            _pad = null;
        }

        /// <summary>Stops the gamepad at once (level end, pause).</summary>
        public void Stop()
        {
            _padStopAt = -1f;
            _pad?.SetMotorSpeeds(0f, 0f);
            _pad = null;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void PurgePage_Vibrate(int milliseconds);

        private static void Phone(int milliseconds) => PurgePage_Vibrate(milliseconds);
#elif UNITY_ANDROID && !UNITY_EDITOR
        // Never true: the reference makes Unity add the VIBRATE permission to the Android manifest.
        private static readonly bool AddPermission = false;

        private void Phone(int milliseconds)
        {
            if (AddPermission) Handheld.Vibrate();
            if (!_vibratorResolved)
            {
                _vibratorResolved = true;
                try
                {
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                catch (System.Exception)
                {
                    _vibrator = null; // No vibrator (or no permission): silently nothing.
                }
            }

            try
            {
                _vibrator?.Call("vibrate", (long)milliseconds);
            }
            catch (System.Exception)
            {
                _vibrator = null;
            }
        }
#else
        private static void Phone(int milliseconds)
        {
        }
#endif
    }
}
