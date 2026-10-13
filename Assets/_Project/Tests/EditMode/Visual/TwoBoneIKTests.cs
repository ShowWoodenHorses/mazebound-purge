using Maze.Presentation.Visual;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Visual
{
    public class TwoBoneIKTests
    {
        private GameObject _root;
        private Transform _upper;
        private Transform _lower;
        private Transform _end;

        [SetUp]
        public void SetUp()
        {
            // Shoulder at the origin, upper arm 0.3 along +X, forearm 0.25 bent toward +Z.
            _root = new GameObject("Arm");
            _upper = _root.transform;
            _lower = new GameObject("Forearm").transform;
            _lower.SetParent(_upper, false);
            _lower.localPosition = new Vector3(0.3f, 0f, 0f);
            _end = new GameObject("Hand").transform;
            _end.SetParent(_lower, false);
            _end.localPosition = new Vector3(0f, 0f, 0.25f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [TestCase(0.2f, 0.1f, 0.3f)]
        [TestCase(0.4f, -0.1f, 0.1f)]
        [TestCase(-0.1f, 0.3f, 0.2f)]
        [TestCase(0.05f, 0f, 0.1f)] // close to the shoulder: the elbow folds
        public void Solve_ReachableTarget_HandOnIt(float x, float y, float z)
        {
            var target = new Vector3(x, y, z);
            TwoBoneIK.Solve(_upper, _lower, _end, target, 1f);

            Assert.Less(Vector3.Distance(_end.position, target), 0.001f);
            Assert.AreEqual(0.3f, Vector3.Distance(_upper.position, _lower.position), 1e-4f, "Bone lengths kept.");
            Assert.AreEqual(0.25f, Vector3.Distance(_lower.position, _end.position), 1e-4f);
        }

        [Test]
        public void Solve_OutOfReach_StretchesTowardTarget()
        {
            var target = new Vector3(0f, 1f, 1f);
            TwoBoneIK.Solve(_upper, _lower, _end, target, 1f);

            Assert.AreEqual(0.55f, _end.position.magnitude, 0.001f, "Arm fully stretched.");
            Assert.Greater(Vector3.Dot(_end.position.normalized, target.normalized), 0.999f);
        }

        [Test]
        public void Solve_HalfWeight_HalfWay()
        {
            var start = _end.position;
            var target = new Vector3(0.2f, 0.1f, 0.3f);
            TwoBoneIK.Solve(_upper, _lower, _end, target, 0.5f);
            Assert.Less(Vector3.Distance(_end.position, Vector3.Lerp(start, target, 0.5f)), 0.001f);
        }

        [Test]
        public void Solve_ZeroWeight_NothingMoves()
        {
            var start = _end.position;
            TwoBoneIK.Solve(_upper, _lower, _end, new Vector3(0.2f, 0.1f, 0.3f), 0f);
            Assert.AreEqual(start, _end.position);
        }
    }
}
