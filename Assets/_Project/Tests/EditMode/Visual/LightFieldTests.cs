using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Lighting;
using Maze.Core.Visibility;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Visual
{
    /// <summary>Light of the level's sources over the grid: falloff, walls and doors, partial rebuild, flicker share.</summary>
    public class LightFieldTests
    {
        private const int Width = 7;
        private const int Height = 3;

        /// <summary>7x3 floor, column x = 3 is wall with a door at (3,1) (closed unless <paramref name="doorOpen"/>).</summary>
        private static bool[] Mask(bool doorOpen)
        {
            var opaque = new bool[Width * Height];
            for (var y = 0; y < Height; y++)
                opaque[y * Width + 3] = y != 1 || !doorOpen;
            return opaque;
        }

        private static LightSourceData Light(float x, float y, float radius = 4f, float flicker = 0f) =>
            new LightSourceData("light_1", new GridPosition(Mathf.RoundToInt(x), Mathf.RoundToInt(y)),
                new Vector2(x - Mathf.RoundToInt(x), y - Mathf.RoundToInt(y)), Color.white, radius, 1f, flicker, false);

        private static float Brightness(LightField field, float x, float y) => field.GetLight(new Vector2(x, y)).r;

        [Test]
        public void Light_FadesWithDistance_StopsAtItsRadius()
        {
            var field = new LightField(Width, Height, new[] { Light(1f, 1f, radius: 2f) });
            field.Rebuild(new CellMaskOpacity(Width, Height, Mask(doorOpen: false)));

            var here = Brightness(field, 1f, 1f);
            var near = Brightness(field, 2f, 1f);
            Assert.That(here, Is.GreaterThan(0.8f));
            Assert.That(near, Is.GreaterThan(0f).And.LessThan(here));

            var small = new LightField(Width, Height, new[] { Light(5f, 1f, radius: 1f) });
            small.Rebuild(new CellMaskOpacity(Width, Height, Mask(doorOpen: false)));
            Assert.AreEqual(0f, Brightness(small, 6.4f, 1f), "Beyond the radius.");
        }

        [Test]
        public void WallFace_IsLit_CellsBehindTheWallAndClosedDoor_AreNot()
        {
            var field = new LightField(Width, Height, new[] { Light(1.4f, 1f) });
            field.Rebuild(new CellMaskOpacity(Width, Height, Mask(doorOpen: false)));

            Assert.That(Brightness(field, 2.6f, 1f), Is.GreaterThan(0f), "The closed door's face is lit.");
            Assert.That(Brightness(field, 2.6f, 0f), Is.GreaterThan(0f), "The wall's face is lit.");
            Assert.AreEqual(0f, Brightness(field, 4f, 1f), "Behind the closed door.");
            Assert.AreEqual(0f, Brightness(field, 4f, 0f), "Behind the wall.");
        }

        [Test]
        public void OpeningADoor_RebuildsOnlyLightsReachingIt()
        {
            var lights = new[] { Light(1.4f, 1f), Light(6f, 2f, radius: 1f) };
            var field = new LightField(Width, Height, lights);
            field.Rebuild(new CellMaskOpacity(Width, Height, Mask(doorOpen: false)));
            Assert.AreEqual(0f, Brightness(field, 4f, 1f));
            var farBefore = Brightness(field, 6f, 2f);
            Assert.That(farBefore, Is.GreaterThan(0.5f));

            var open = new CellMaskOpacity(Width, Height, Mask(doorOpen: true));
            var region = field.RebuildAround(new GridPosition(3, 1), open);

            Assert.That(region.width, Is.GreaterThan(0));
            Assert.That(region.xMax, Is.LessThanOrEqualTo(Mathf.CeilToInt((1.4f + 4f + 0.5f) * LightField.TexelsPerCell)),
                "Only the first light's area.");
            Assert.That(Brightness(field, 4f, 1f), Is.GreaterThan(0f), "Light passes the open door.");
            Assert.AreEqual(farBefore, Brightness(field, 6f, 2f), 1e-6f, "The far light is kept.");
        }

        [Test]
        public void Pixels_EncodeLightAndFlickerShare()
        {
            var field = new LightField(Width, Height, new[] { Light(1f, 1f, flicker: 1f), Light(5f, 1f, flicker: 0f) });
            field.Rebuild(new CellMaskOpacity(Width, Height, Mask(doorOpen: false)));

            Color32 PixelAt(float x, float y)
            {
                var tx = Mathf.FloorToInt((x + 0.5f) * LightField.TexelsPerCell);
                var ty = Mathf.FloorToInt((y + 0.5f) * LightField.TexelsPerCell);
                return field.Pixels[ty * field.TextureWidth + tx];
            }

            var flickering = PixelAt(1f, 1f);
            var steady = PixelAt(5f, 1f);
            Assert.That(flickering.r, Is.GreaterThan(80), "About 1 / MaxLight near the light.");
            Assert.AreEqual(255, flickering.a);
            Assert.That(steady.r, Is.GreaterThan(80));
            Assert.AreEqual(0, steady.a);
        }
    }
}
