using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace Fomoxa.Unity.Tests
{
    public sealed class PolygonDecompositionTest
    {
        private static readonly Vector2[] LShape =
        {
            new Vector2(0f, 0f),
            new Vector2(2f, 0f),
            new Vector2(2f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 2f),
            new Vector2(0f, 2f),
        };

        [Test]
        public void AConvexPathStaysOnePieceTurnedCounterClockwise()
        {
            var clockwise = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };

            List<Vector2[]> pieces = PolygonDecomposition.Split(clockwise);

            Assert.AreEqual(1, pieces.Count);
            Assert.AreEqual(4, pieces[0].Length);
            Assert.Greater(TwiceArea(pieces[0]), 0f);
            Assert.AreEqual(2f, TwiceArea(pieces[0]));
        }

        [Test]
        public void AConcavePathSplitsIntoConvexPiecesCoveringTheSameArea()
        {
            List<Vector2[]> pieces = PolygonDecomposition.Split(LShape);

            Assert.AreEqual(2, pieces.Count);
            float total = 0f;
            foreach (Vector2[] piece in pieces)
            {
                AssertConvex(piece);
                total += TwiceArea(piece);
            }

            Assert.AreEqual(6f, total);
        }

        [Test]
        public void TheSameConcavePathGivesTheSamePieces()
        {
            List<Vector2[]> first = PolygonDecomposition.Split(LShape);
            var reversed = (Vector2[])LShape.Clone();
            Array.Reverse(reversed);
            List<Vector2[]> second = PolygonDecomposition.Split(LShape);

            Assert.AreEqual(first.Count, second.Count);
            for (int index = 0; index < first.Count; index++)
            {
                CollectionAssert.AreEqual(first[index], second[index]);
            }

            Assert.AreEqual(2, PolygonDecomposition.Split(reversed).Count);
        }

        [Test]
        public void RepeatedAndCollinearPointsAreDropped()
        {
            var path = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(2f, 0f),
                new Vector2(2f, 2f),
                new Vector2(0f, 2f),
                new Vector2(0f, 0f),
            };

            List<Vector2> outline = PolygonDecomposition.Outline(path);

            CollectionAssert.AreEqual(new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f), new Vector2(0f, 2f) }, outline);
        }

        [Test]
        public void ACrossingOrFlatPathIsRefused()
        {
            var crossing = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 1f) };
            var flat = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f) };

            Assert.Throws<ArgumentException>(() => PolygonDecomposition.Split(crossing));
            Assert.Throws<ArgumentException>(() => PolygonDecomposition.Split(flat));
            Assert.Throws<ArgumentNullException>(() => PolygonDecomposition.Split(null));
        }

        private static void AssertConvex(Vector2[] polygon)
        {
            for (int index = 0; index < polygon.Length; index++)
            {
                Vector2 a = polygon[(index + polygon.Length - 1) % polygon.Length];
                Vector2 b = polygon[index];
                Vector2 c = polygon[(index + 1) % polygon.Length];
                Assert.GreaterOrEqual((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X), 0f);
            }
        }

        private static float TwiceArea(Vector2[] polygon)
        {
            float sum = 0f;
            for (int index = 0; index < polygon.Length; index++)
            {
                Vector2 current = polygon[index];
                Vector2 next = polygon[(index + 1) % polygon.Length];
                sum += current.X * next.Y - next.X * current.Y;
            }

            return sum;
        }
    }
}
