using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Services;
using Maze.Core.Common;

namespace Maze.Application.Platform
{
    /// <summary>
    /// One analytics backend of a platform (Yandex Metrica on Yandex Games, MyTracker on Android, a log in the
    /// editor…). Gets ready-made events; knows nothing about the game. A new platform = a new sink.
    /// </summary>
    public interface IAnalyticsSink
    {
        string Name { get; }

        /// <summary>Called once at start (SDK init); must not throw.</summary>
        UniTask InitializeAsync(CancellationToken cancellation);

        /// <param name="parameters">Event parameters (string, int, float, bool values); never null.</param>
        void Send(string eventName, IReadOnlyDictionary<string, object> parameters);
    }

    /// <summary>Event and parameter names: stable strings shared by every platform (dashboards rely on them).</summary>
    public static class AnalyticsEvents
    {
        public const string LevelStart = "level_start";
        public const string LevelComplete = "level_complete";
        public const string LevelFail = "level_fail";
        public const string LevelQuit = "level_quit";
        public const string HintUsed = "hint_used";
        public const string MapLayerUnlocked = "map_layer_unlocked";
        public const string Ad = "ad";
        public const string WeaponPickup = "weapon_pickup";
        public const string SettingsChanged = "settings_changed";
        public const string Performance = "performance";
        public const string SessionStart = "session_start";
        public const string TutorialStep = "tutorial_step";
        public const string SignIn = "sign_in";
    }

    /// <summary>
    /// Analytics of the game (all platforms): high-level methods build an event and hand it to every
    /// <see cref="IAnalyticsSink"/>. A failing sink never breaks the game (logged). Not for per-frame use.
    /// </summary>
    public sealed class AnalyticsService : IApplicationService
    {
        private readonly IReadOnlyList<IAnalyticsSink> _sinks;
        private readonly Dictionary<string, object> _parameters = new Dictionary<string, object>(StringComparer.Ordinal);

        public AnalyticsService(IReadOnlyList<IAnalyticsSink> sinks)
        {
            _sinks = sinks ?? Array.Empty<IAnalyticsSink>();
        }

        public string Name => "Analytics";

        /// <summary>Events sent so far in this session (tests).</summary>
        public int SentCount { get; private set; }

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            foreach (var sink in _sinks)
            {
                try
                {
                    await sink.InitializeAsync(cancellation);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    GameLog.Exception(LogChannel.Analytics, e, $"Analytics sink '{sink.Name}' failed to initialize");
                }
            }
        }

        /// <summary>Starts an event: fill the returned parameters, then <see cref="Send"/>.</summary>
        public Dictionary<string, object> Begin()
        {
            _parameters.Clear();
            return _parameters;
        }

        public void Send(string eventName)
        {
            SentCount++;
            foreach (var sink in _sinks)
            {
                try
                {
                    sink.Send(eventName, _parameters);
                }
                catch (Exception e)
                {
                    GameLog.Exception(LogChannel.Analytics, e, $"Analytics sink '{sink.Name}' failed on '{eventName}'");
                }
            }

            _parameters.Clear();
        }

        public void Track(string eventName)
        {
            Begin();
            Send(eventName);
        }
    }

    /// <summary>Writes events to the log (editor, development builds, platforms without analytics).</summary>
    public sealed class LogAnalyticsSink : IAnalyticsSink
    {
        public string Name => "Log";

        public UniTask InitializeAsync(CancellationToken cancellation) => UniTask.CompletedTask;

        public void Send(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            var text = new System.Text.StringBuilder(eventName);
            foreach (var pair in parameters) text.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
            GameLog.Info(LogChannel.Analytics, text.ToString());
        }
    }
}
