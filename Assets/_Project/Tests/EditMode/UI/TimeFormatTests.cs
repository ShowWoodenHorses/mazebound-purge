using Maze.Presentation.UI;
using NUnit.Framework;

namespace Maze.Tests.EditMode.UI
{
    public class TimeFormatTests
    {
        [TestCase(0f, "0:00")]
        [TestCase(-3f, "0:00")]
        [TestCase(float.NaN, "0:00")]
        [TestCase(59.9f, "0:59")]
        [TestCase(61f, "1:01")]
        [TestCase(754f, "12:34")]
        [TestCase(3600f, "1:00:00")]
        [TestCase(3725f, "1:02:05")]
        public void ToText_MinutesSeconds_HoursFromAnHour(float seconds, string expected) =>
            Assert.AreEqual(expected, TimeFormat.ToText(seconds));

        [Test]
        public void Write_FitsTheLongestTime()
        {
            var buffer = new char[TimeFormat.MaxLength];
            var length = TimeFormat.Write(TimeFormat.WholeSeconds(1e9f), buffer);
            Assert.AreEqual("999:59:59", new string(buffer, 0, length));
        }
    }
}
