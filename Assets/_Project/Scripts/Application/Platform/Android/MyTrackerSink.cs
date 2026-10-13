using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using Mycom.Tracker.Unity;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Game events to MyTracker (the Android build; plugin in <c>Assets/Plugins/MyTracker</c>): the event's name and
    /// its parameters as strings (MyTracker takes string pairs, each at most 64 characters). Without an SDK key
    /// (<see cref="RuStoreKeys.MyTrackerSdkKey"/>) it stays off; the log sink keeps writing the events. Launches and
    /// sessions are MyTracker's own (automatic); events are buffered and sent in batches.
    /// </summary>
    public sealed class MyTrackerSink : IAnalyticsSink
    {
        /// <summary>MyTracker's limit for an event name, a parameter key and a value.</summary>
        public const int MaxLength = 64;

        private readonly Dictionary<string, string> _parameters = new Dictionary<string, string>(16);
        private bool _active;

        public string Name => "MyTracker";

        public UniTask InitializeAsync(CancellationToken cancellation)
        {
            var key = RuStoreKeys.MyTrackerSdkKey.Trim();
            if (key.Length == 0)
            {
                GameLog.Info(LogChannel.Analytics, "MyTracker: no SDK key (RuStoreKeys) — off.");
                return UniTask.CompletedTask;
            }

            MyTracker.IsDebugMode = UnityEngine.Debug.isDebugBuild;
            var config = MyTracker.MyTrackerConfig;
            if (config != null) config.IsTrackingLocationEnabled = false;
            MyTracker.Init(key);
            _active = true;
            return UniTask.CompletedTask;
        }

        public void Send(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            if (!_active) return;
            if (parameters.Count == 0) MyTracker.TrackEvent(Clip(eventName));
            else MyTracker.TrackEvent(Clip(eventName), ToStrings(parameters, _parameters));
        }

        /// <summary>Parameters as MyTracker's string pairs: numbers invariant, bools true/false, all clipped to 64.</summary>
        public static Dictionary<string, string> ToStrings(IReadOnlyDictionary<string, object> parameters,
            Dictionary<string, string> buffer = null)
        {
            var result = buffer ?? new Dictionary<string, string>(parameters.Count);
            result.Clear();
            foreach (var pair in parameters)
                result[Clip(pair.Key)] = Clip(ToText(pair.Value));
            return result;
        }

        private static string ToText(object value)
        {
            switch (value)
            {
                case null:
                    return "";
                case bool b:
                    return b ? "true" : "false";
                case float f:
                    return ToText((double)f);
                case double d:
                    return double.IsNaN(d) || double.IsInfinity(d) ? "" : d.ToString("0.###", CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        }

        private static string Clip(string text) => text == null || text.Length <= MaxLength ? text ?? "" : text.Substring(0, MaxLength);
    }
}
