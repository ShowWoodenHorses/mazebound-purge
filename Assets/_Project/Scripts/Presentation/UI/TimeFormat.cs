using System;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Play time as "m:ss" ("h:mm:ss" from an hour), the same in every language (digits and a colon only).
    /// <see cref="Write"/> fills a char buffer without allocating (the HUD timer, once a second).
    /// </summary>
    public static class TimeFormat
    {
        /// <summary>Enough for "999:59:59".</summary>
        public const int MaxLength = 9;

        /// <summary>Whole seconds shown for <paramref name="seconds"/> (cut, not rounded: 59.9 is still 0:59).</summary>
        public static int WholeSeconds(float seconds) =>
            float.IsNaN(seconds) || seconds <= 0f ? 0 : (int)Math.Min(seconds, 999f * 3600f + 3599f);

        public static string ToText(float seconds)
        {
            var buffer = new char[MaxLength];
            return new string(buffer, 0, Write(WholeSeconds(seconds), buffer));
        }

        /// <summary>Writes <paramref name="totalSeconds"/> into <paramref name="buffer"/>; returns the length.</summary>
        public static int Write(int totalSeconds, char[] buffer)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            var hours = totalSeconds / 3600;
            var minutes = totalSeconds / 60 % 60;
            var seconds = totalSeconds % 60;
            var length = 0;
            if (hours > 0)
            {
                length = WriteNumber(hours, buffer, length);
                buffer[length++] = ':';
                buffer[length++] = (char)('0' + minutes / 10);
                buffer[length++] = (char)('0' + minutes % 10);
            }
            else
            {
                length = WriteNumber(minutes, buffer, length);
            }

            buffer[length++] = ':';
            buffer[length++] = (char)('0' + seconds / 10);
            buffer[length++] = (char)('0' + seconds % 10);
            return length;
        }

        private static int WriteNumber(int value, char[] buffer, int at)
        {
            var digits = value >= 100 ? 3 : value >= 10 ? 2 : 1;
            for (var i = digits - 1; i >= 0; i--)
            {
                buffer[at + i] = (char)('0' + value % 10);
                value /= 10;
            }

            return at + digits;
        }
    }
}
