using System;
using System.Numerics;

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

	public struct VertexTL
	{
		public Vector3 Position;
		public Vector2 UV;
		public Vector2 LightmapUV;
	}
}