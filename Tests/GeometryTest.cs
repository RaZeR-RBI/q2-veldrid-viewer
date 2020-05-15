using System;
using System.Drawing;
using System.Numerics;
using Common;
using FluentAssertions;
using Xunit;

namespace Tests
{
	public class GeometryTest
	{
		private static Plane[] s_box = new Plane[] {
			new Plane(Vector3.UnitX, 2f),
			new Plane(-Vector3.UnitX, 1f),
			new Plane(Vector3.UnitY, 3f),
			new Plane(-Vector3.UnitY, 1f),
			new Plane(Vector3.UnitZ, 4f),
			new Plane(-Vector3.UnitZ, 1f),
		};

		[Theory]
		[InlineData(0, 2, 4, true)]
		[InlineData(1, 3, 5, true)]
		[InlineData(1, 4, 2, true)]
		[InlineData(0, 1, 3, false)]
		[InlineData(0, 0, 0, false)]
		public void CheckIf3PlanesIntersectionIsAPoint(int aIndex, int bIndex, int cIndex, bool expected)
		{
			var a = s_box[aIndex];
			var b = s_box[bIndex];
			var c = s_box[cIndex];
			Geometry.IntersectionIsPoint(a, b, c).Should().Be(expected);
		}

		[Theory]
		[InlineData(0, 2, 4, 2f, 3f, 4f)]
		[InlineData(1, 3, 5, -1f, -1f, -1f)]
		[InlineData(1, 4, 2, -1f, 3f, 4f)]
		public void GetIntersectionPointBetween3Planes(int aIndex, int bIndex, int cIndex, float expectedX, float expectedY, float expectedZ)
		{
			var a = s_box[aIndex];
			var b = s_box[bIndex];
			var c = s_box[cIndex];
			var expected = new Vector3(expectedX, expectedY, expectedZ);
			Geometry.GetIntersectionPoint(a, b, c).Should().Be(expected);
		}

		[Theory]
		[InlineData(0f, 0f, 0f, true)]
		[InlineData(1f, 1.5f, 2f, true)]
		[InlineData(2f, 3f, 4f, true)]
		[InlineData(-1f, -1f, -1f, true)]
		[InlineData(2.1f, 3.1f, 4.1f, false)]
		[InlineData(-1.1f, -1.1f, -1.1f, false)]
		public void CheckPointsAgainstHull(float pX, float pY, float pZ, bool expected)
		{
			Geometry.PointInHull(new Vector3(pX, pY, pZ), s_box).Should().Be(expected);
		}

		[Fact]
		public void ShouldCalculatePointCloud()
		{
			var planeCount = s_box.Length;
			Span<Vector3> points = stackalloc Vector3[Geometry.GetMaxHullCorners(planeCount)];
			Geometry.GetConvexHullPointCloud(s_box, ref points, out int count);
			count.Should().Be(8);
			var pointArray = points.ToArray();
			pointArray.Should().Contain(new Vector3(2f, 3f, 4f));
			pointArray.Should().Contain(new Vector3(-1f, 3f, 4f));
			pointArray.Should().Contain(new Vector3(2f, -1f, 4f));
			pointArray.Should().Contain(new Vector3(-1f, -1f, 4f));
			pointArray.Should().Contain(new Vector3(2f, 3f, -1f));
			pointArray.Should().Contain(new Vector3(-1f, 3f, -1f));
			pointArray.Should().Contain(new Vector3(2f, -1f, -1f));
			pointArray.Should().Contain(new Vector3(-1f, -1f, -1f));
		}

		[Fact]
		public void ShouldBuildConvexHullFace()
		{
			var faceIndex = 0;
			var planeCount = s_box.Length;
			Span<Vector3> verts = stackalloc Vector3[Geometry.GetMaxVertsPerFaceForHull(planeCount)];

			Geometry.BuildConvexHullFace(s_box, faceIndex, ref verts, out int count);
			count.Should().Be(4);

			var center = Geometry.CenterPoint(verts.Slice(0, count));
			var normal = s_box[faceIndex].Normal;
			Geometry.IsCCW(verts[0], verts[1], center, normal).Should().BeTrue();
			Geometry.IsCCW(verts[1], verts[2], center, normal).Should().BeTrue();
			Geometry.IsCCW(verts[2], verts[3], center, normal).Should().BeTrue();
			Geometry.IsCCW(verts[3], verts[0], center, normal).Should().BeTrue();
		}
	}
}