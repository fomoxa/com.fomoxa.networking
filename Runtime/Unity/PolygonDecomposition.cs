using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Unity
{
    internal static class PolygonDecomposition
    {
        public static List<Vector2> Outline(IReadOnlyList<Vector2> path)
        {
            List<Vector2> points = Clean(path);
            if (points.Count < 3)
            {
                throw new ArgumentException("the polygon path has fewer than 3 distinct points that are not collinear", nameof(path));
            }

            if (TwiceArea(points) < 0d)
            {
                points.Reverse();
            }

            if (CrossesItself(points))
            {
                throw new ArgumentException("the polygon path crosses itself", nameof(path));
            }

            return points;
        }

        public static List<Vector2[]> Split(IReadOnlyList<Vector2> path)
        {
            List<Vector2> points = Outline(path);
            List<List<int>> pieces = Triangulate(points);
            Merge(pieces, points);
            var convex = new List<Vector2[]>(pieces.Count);
            foreach (List<int> piece in pieces)
            {
                var polygon = new Vector2[piece.Count];
                for (int index = 0; index < polygon.Length; index++)
                {
                    polygon[index] = points[piece[index]];
                }

                convex.Add(polygon);
            }

            return convex;
        }

        private static List<Vector2> Clean(IReadOnlyList<Vector2> path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            var points = new List<Vector2>(path.Count);
            foreach (Vector2 point in path)
            {
                if (points.Count == 0 || points[points.Count - 1] != point)
                {
                    points.Add(point);
                }
            }

            while (points.Count > 1 && points[0] == points[points.Count - 1])
            {
                points.RemoveAt(points.Count - 1);
            }

            bool removed = true;
            while (removed && points.Count >= 3)
            {
                removed = false;
                for (int index = 0; index < points.Count; index++)
                {
                    Vector2 previous = points[(index + points.Count - 1) % points.Count];
                    Vector2 next = points[(index + 1) % points.Count];
                    if (Turn(previous, points[index], next) == 0d)
                    {
                        points.RemoveAt(index);
                        removed = true;
                        break;
                    }
                }
            }

            return points;
        }

        private static List<List<int>> Triangulate(List<Vector2> points)
        {
            var remaining = new List<int>(points.Count);
            for (int index = 0; index < points.Count; index++)
            {
                remaining.Add(index);
            }

            var triangles = new List<List<int>>();
            while (remaining.Count > 3)
            {
                bool clipped = false;
                for (int position = 0; position < remaining.Count; position++)
                {
                    int previous = remaining[(position + remaining.Count - 1) % remaining.Count];
                    int current = remaining[position];
                    int next = remaining[(position + 1) % remaining.Count];
                    if (Turn(points[previous], points[current], points[next]) <= 0d || ContainsAnother(points, remaining, previous, current, next))
                    {
                        continue;
                    }

                    triangles.Add(new List<int> { previous, current, next });
                    remaining.RemoveAt(position);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    throw new ArgumentException("the polygon path could not be split into convex pieces");
                }
            }

            triangles.Add(remaining);
            return triangles;
        }

        private static void Merge(List<List<int>> pieces, List<Vector2> points)
        {
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int first = 0; first < pieces.Count && !merged; first++)
                {
                    for (int second = first + 1; second < pieces.Count && !merged; second++)
                    {
                        if (TryJoin(pieces[first], pieces[second], points, out List<int> joined))
                        {
                            pieces[first] = joined;
                            pieces.RemoveAt(second);
                            merged = true;
                        }
                    }
                }
            }
        }

        private static bool TryJoin(List<int> first, List<int> second, List<Vector2> points, out List<int> joined)
        {
            joined = null;
            for (int edge = 0; edge < first.Count; edge++)
            {
                int start = first[edge];
                int end = first[(edge + 1) % first.Count];
                int shared = IndexOfEdge(second, end, start);
                if (shared < 0)
                {
                    continue;
                }

                var candidate = new List<int>(first.Count + second.Count - 2);
                for (int step = 1; step <= first.Count; step++)
                {
                    candidate.Add(first[(edge + step) % first.Count]);
                }

                for (int step = 2; step < second.Count; step++)
                {
                    candidate.Add(second[(shared + step) % second.Count]);
                }

                if (!IsConvex(candidate, points))
                {
                    return false;
                }

                joined = candidate;
                return true;
            }

            return false;
        }

        private static int IndexOfEdge(List<int> polygon, int start, int end)
        {
            for (int index = 0; index < polygon.Count; index++)
            {
                if (polygon[index] == start && polygon[(index + 1) % polygon.Count] == end)
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool IsConvex(List<int> polygon, List<Vector2> points)
        {
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 previous = points[polygon[(index + polygon.Count - 1) % polygon.Count]];
                Vector2 next = points[polygon[(index + 1) % polygon.Count]];
                if (Turn(previous, points[polygon[index]], next) < 0d)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsAnother(List<Vector2> points, List<int> remaining, int a, int b, int c)
        {
            foreach (int index in remaining)
            {
                if (index == a || index == b || index == c)
                {
                    continue;
                }

                Vector2 point = points[index];
                if (Turn(points[a], points[b], point) >= 0d && Turn(points[b], points[c], point) >= 0d && Turn(points[c], points[a], point) >= 0d)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CrossesItself(List<Vector2> points)
        {
            int count = points.Count;
            for (int first = 0; first < count; first++)
            {
                for (int second = first + 1; second < count; second++)
                {
                    if (second == first + 1 || (first == 0 && second == count - 1))
                    {
                        continue;
                    }

                    if (Touch(points[first], points[(first + 1) % count], points[second], points[(second + 1) % count]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Touch(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            double abc = Turn(a, b, c);
            double abd = Turn(a, b, d);
            double cda = Turn(c, d, a);
            double cdb = Turn(c, d, b);
            if (((abc > 0d && abd < 0d) || (abc < 0d && abd > 0d)) && ((cda > 0d && cdb < 0d) || (cda < 0d && cdb > 0d)))
            {
                return true;
            }

            return (abc == 0d && Within(a, b, c)) || (abd == 0d && Within(a, b, d)) || (cda == 0d && Within(c, d, a)) || (cdb == 0d && Within(c, d, b));
        }

        private static bool Within(Vector2 a, Vector2 b, Vector2 point) =>
            Math.Min(a.X, b.X) <= point.X && point.X <= Math.Max(a.X, b.X) && Math.Min(a.Y, b.Y) <= point.Y && point.Y <= Math.Max(a.Y, b.Y);

        private static double TwiceArea(List<Vector2> points)
        {
            double sum = 0d;
            for (int index = 0; index < points.Count; index++)
            {
                Vector2 current = points[index];
                Vector2 next = points[(index + 1) % points.Count];
                sum += (double)current.X * next.Y - (double)next.X * current.Y;
            }

            return sum;
        }

        private static double Turn(Vector2 a, Vector2 b, Vector2 c)
        {
            float firstX = b.X - a.X;
            float firstY = b.Y - a.Y;
            float secondX = c.X - b.X;
            float secondY = c.Y - b.Y;
            return (double)firstX * secondY - (double)firstY * secondX;
        }
    }
}
