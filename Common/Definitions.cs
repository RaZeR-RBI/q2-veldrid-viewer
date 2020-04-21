using System;
using System.Diagnostics;
using System.Numerics;
using Veldrid;

namespace Common
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

	public struct VertexNT
	{
		public Vector3 Position;
		public Vector3 Normal;
		public Vector2 UV;

		public const int SizeInBytes = 12 * 2 + 8;
	}

	public struct VertexColor
	{
		public Vector3 Position;
		public Vector3 Color;

		public const uint SizeInBytes = 12 * 2;

		public VertexColor(Vector3 pos, RgbaFloat clr) =>
			(Position, Color) = (pos, new Vector3(clr.R, clr.G, clr.B));
	}

	public struct VATVertex
	{
		public int Index;
		public Vector2 UV;

		public const uint SizeInBytes = 4 + 4 * 2;
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

	public struct Vector2i
	{
		public int X;
		public int Y;

		public Vector2i(int x, int y) => (X, Y) = (x, y);

		public override string ToString() => $"{{ {X}, {Y} }}";

		public static Vector2i operator +(Vector2i vector) => vector;
		public static Vector2i operator -(Vector2i vector) => new Vector2i(-vector.X, -vector.Y);

		public static Vector2i operator +(Vector2i a, Vector2i b) =>
			new Vector2i(a.X + b.X, a.Y + b.Y);

		public static Vector2i operator -(Vector2i a, Vector2i b) =>
			new Vector2i(a.X - b.X, a.Y - b.Y);

		public static Vector2i operator *(Vector2i v, int t) =>
			new Vector2i(v.X * t, v.Y * t);

		public static Vector2i operator /(Vector2i v, int t) =>
			new Vector2i(v.X / t, v.Y / t);

		public static Vector2 operator /(Vector2i v, float t) =>
			new Vector2((float)v.X / t, (float)v.Y / t);

		public static Vector2i Min(Vector2i a, Vector2i b) =>
			new Vector2i(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));

		public static Vector2i Max(Vector2i a, Vector2i b) =>
			new Vector2i(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

		public static explicit operator Vector2(Vector2i v) =>
			new Vector2((float)v.X, (float)v.Y);
	}

	public struct ColorRGBA
	{
		public byte R;
		public byte G;
		public byte B;
		public byte A;

		public ColorRGBA(byte r, byte g, byte b) =>
			(R, G, B, A) = (r, g, b, 255);

		public ColorRGBA(Vector3 normalized) =>
			(R, G, B, A) = (
				(byte)(normalized.X * 255f),
				(byte)(normalized.Y * 255f),
				(byte)(normalized.Z * 255f),
				 255);
	}

	public struct ColorRGBA16
	{
		public ushort R;
		public ushort G;
		public ushort B;
		public ushort A;

		private const float c_max = (float)ushort.MaxValue;

		public ColorRGBA16(Vector3 normalized) =>
			(R, G, B, A) = (
				(ushort)(normalized.X * c_max),
				(ushort)(normalized.Y * c_max),
				(ushort)(normalized.Z * c_max),
				 ushort.MaxValue);
	}
}