using System;
using System.Numerics;

namespace Common
{
	public static class Geometry
	{
		private const float c_margin = 1E-3f;

		public static bool IntersectionIsPoint(Plane a, Plane b, Plane c)
		{
			var cross = Vector3.Cross(b.Normal, c.Normal);
			if (cross.LengthSquared() <= c_margin) return false;
			return MathF.Abs(Vector3.Dot(a.Normal, cross)) > c_margin;
		}

		public static Vector3 GetIntersectionPoint(Plane a, Plane b, Plane c, bool check = true)
		{
			var cross23 = Vector3.Cross(b.Normal, c.Normal);
			var denom = Vector3.Dot(a.Normal, cross23);
			if (check && MathF.Abs(denom) <= c_margin)
				return new Vector3(float.NaN, float.NaN, float.NaN);

			var cross31 = Vector3.Cross(c.Normal, a.Normal);
			var cross12 = Vector3.Cross(a.Normal, b.Normal);
			var nom = a.D * cross23 + b.D * cross31 + c.D * cross12;
			return nom / denom;
		}

		public static bool PointInHull(Vector3 point, ReadOnlySpan<Plane> planes)
		{
			for (var i = 0; i < planes.Length; i++)
			{
				var plane = planes[i];
				var dist = Vector3.Dot(point, plane.Normal) - plane.D;
				if (dist > c_margin) return false;
			}
			return true;
		}

		// not sure if it's correct
		public static int GetMaxHullCorners(int planeCount) => planeCount * (planeCount - 1);

		public static int GetMaxVertsPerFaceForHull(int planeCount) => (planeCount - 1) * planeCount;

		public static int GetMaxVertsForHull(int planeCount) =>
			GetMaxVertsPerFaceForHull(planeCount) * planeCount;

		public static int GetMaxTrianglesForHull(int planeCount) =>
			(GetMaxVertsPerFaceForHull(planeCount) - 2) * planeCount;

		public static void GetConvexHullPointCloud(ReadOnlySpan<Plane> planes, ref Span<Vector3> result, out int count, bool checks = true)
		{
			if (checks && GetMaxHullCorners(planes.Length) > result.Length)
				throw new ArgumentOutOfRangeException("The result span is too small to contain potential points");
			if (checks && planes.Length < 4)
				throw new ArgumentOutOfRangeException("At least four planes should be specified");

			count = 0;
			for (var i = 0; i < planes.Length; i++)
			{
				var a = planes[i];
				for (var j = i + 1; j < planes.Length; j++)
				{
					var b = planes[j];
					for (var k = j + 1; k < planes.Length; k++)
					{
						var c = planes[k];
						var p = GetIntersectionPoint(a, b, c, check: true);
						if (float.IsNaN(p.X)) continue;
						if (!PointInHull(p, planes)) continue;
						var exists = false;
						for (var m = 0; m < count; m++)
							if (result[m] == p)
							{
								exists = true;
								break;
							}
						if (exists) continue;
						result[count] = p;
						count++;
					}
				}
			}
		}

		public static Vector3 CenterPoint(ReadOnlySpan<Vector3> points)
		{
			var center = Vector3.Zero;
			for (var i = 0; i < points.Length; i++)
				center += points[i];
			return center / (float)points.Length;
		}

		public static void BuildConvexHullFace(ReadOnlySpan<Plane> planes, int index, ref Span<Vector3> result, out int count, bool checks = true)
		{
			if (checks && GetMaxVertsPerFaceForHull(planes.Length) > result.Length)
				throw new ArgumentOutOfRangeException("The result span is too small to contain potential points");
			if (checks && planes.Length < 4)
				throw new ArgumentOutOfRangeException("At least four planes should be specified");

			var plane = planes[index];

			// step 1 - collect all points
			count = 0;
			for (var i = 0; i < planes.Length; i++)
			{
				if (i == index) continue;
				var b = planes[i];
				for (var j = i + 1; j < planes.Length; j++)
				{
					if (j == index) continue;
					var c = planes[j];
					var p = GetIntersectionPoint(plane, b, c, check: true);
					if (float.IsNaN(p.X)) continue;
					if (!PointInHull(p, planes)) continue;
					result[count] = p;
					count++;
				}
			}
			if (count < 3)
			{
				count = 0;
				return;
			}
			// Step 2 - sort them in counterclockwise order
			var center = CenterPoint(result.Slice(0, count));
			// do an insertion sort
			for (var i = 1; i < count; i++)
			{
				var x = result[i];
				var j = i - 1;
				while (j >= 0 && IsCCW(x, result[j], center, plane.Normal))
				{
					result[j + 1] = result[j];
					j--;
				}
				result[j + 1] = x;
			}
		}

		public static bool IsCCW(Vector3 a, Vector3 b, Vector3 center, Vector3 normal)
		{
			var cross = Vector3.Cross(center - a, center - b);
			return Vector3.Dot(cross, normal) <= c_margin;
		}

		public static int GetTriangleCount<T>(ReadOnlySpan<T> faceVerts) =>
			faceVerts.Length - 2;

		public static int GetTriangleCount(int vertexCount) =>
			vertexCount - 2;

		public static void Triangulate<T>(ReadOnlySpan<T> faceVerts,
			Span<T> result)
		{
			var count = faceVerts.Length - 2;
			var root = faceVerts[0];
			var offset = 1;
			for (var i = 0; i < count * 3; i += 3)
			{
				result[i] = root;
				result[i + 1] = faceVerts[offset];
				result[i + 2] = faceVerts[offset + 1];
				offset++;
			}
		}
	}
}