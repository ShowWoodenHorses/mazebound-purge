using System;
using UnityEngine;

namespace Maze.Core.Common
{
    /// <summary>Log channels from ТЗ §108. The channel is written as a tag so logs can be filtered.</summary>
    public enum LogChannel
    {
        Bootstrap,
        GameFlow,
        Addressables,
        LevelLoading,
        LevelUnloading,
        Validation,
        Save,
        Gameplay,
        Visual,
        Localization,
        Analytics,
    }

    /// <summary>
    /// Structured logging (ТЗ §108): "[Maze][Channel] message". Stateless; meant for lifecycle events,
    /// not for per-frame loops.
    /// </summary>
    public static class GameLog
    {
        /// <summary>
        /// Informational message. In a release player it is written without a stack trace: capturing one costs far
        /// more than the message, and gameplay info (pickups, kills) is logged during play.
        /// </summary>
        public static void Info(LogChannel channel, string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(Format(channel, message));
#else
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", Format(channel, message));
#endif
        }

        public static void Warning(LogChannel channel, string message) => Debug.LogWarning(Format(channel, message));

        public static void Error(LogChannel channel, string message) => Debug.LogError(Format(channel, message));

        public static void Exception(LogChannel channel, Exception exception, string message)
        {
            Debug.LogError(Format(channel, message + ": " + exception.Message));
            Debug.LogException(exception);
        }

        private static string Format(LogChannel channel, string message) => "[Maze][" + channel + "] " + message;
    }
}
