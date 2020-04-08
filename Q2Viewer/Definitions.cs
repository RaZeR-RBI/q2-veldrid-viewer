using System;
using System.Diagnostics;
using System.Numerics;
using Veldrid;

namespace Q2Viewer
{
	[Flags]
	public enum SurfaceRenderFlags : int
	{
		None = 0,
		PlaneBack = 2,
		DrawSky = 4,
		DrawWarp = 0x10,
		DrawBackground = 0x40,
		IsUnderwater = 0x80
	}

	public struct VertexNTL
	{
		public Vector3 Position;
		public Vector3 Normal;
		public Vector2 UV;
		public Vector2 LightmapUV;

		public const int SizeInBytes = 12 * 2 + 8 * 2;
	}

	public struct VertexColor
	{
		public Vector3 Position;
		public Vector3 Color;

		public const uint SizeInBytes = 12 * 2;

		public VertexColor(Vector3 pos, RgbaFloat clr) =>
			(Position, Color) = (pos, new Vector3(clr.R, clr.G, clr.B));
	}

	public struct AABB
	{
		public Vector3 Min;
		public Vector3 Max;

		public AABB(Vector3 min, Vector3 max) => (Min, Max) = (min, max);

		public void GetVertices(ref Span<Vector4> vertices)
		{
			Debug.Assert(vertices.Length >= 8);
			vertices[0] = new Vector4(Min, 1);
			vertices[1] = new Vector4(Max, 1);
			vertices[2] = new Vector4(Min.X, Min.Y, Max.Z, 1);
			vertices[3] = new Vector4(Min.X, Max.Y, Min.Z, 1);
			vertices[4] = new Vector4(Max.X, Min.Y, Min.Z, 1);
			vertices[5] = new Vector4(Min.X, Max.Y, Max.Z, 1);
			vertices[6] = new Vector4(Max.X, Min.Y, Max.Z, 1);
			vertices[7] = new Vector4(Max.X, Max.Y, Min.Z, 1);
		}
	}
}