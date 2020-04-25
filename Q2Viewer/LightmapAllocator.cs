using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Common;
using Veldrid;

namespace Q2Viewer
{
	public class LightmapAllocator
	{
		public const uint BlockSize = 4096;
		public const uint LightmapsPerFace = 4;

		private const uint c_padding = 0;
		private const int c_lightmapScale = 16;
		private const float c_coordScale = BlockSize * c_lightmapScale;
		private const float c_halfPixel = 0.5f / (float)BlockSize;
		private static readonly Vector2 s_halfPixelVec = new Vector2(c_halfPixel, c_halfPixel);
		private static readonly Vector2 s_pixelVec = new Vector2(2f * c_halfPixel, 2f * c_halfPixel);

		private struct LightmapAllocData
		{
			public Texture Staging;
			public uint RowHeight;
			public uint CurrentU;
			public uint CurrentV;

			public LightmapAllocData(Texture texture) =>
				(Staging, RowHeight, CurrentU, CurrentV) = (texture, c_padding, c_padding, c_padding);

			public static Vector2i GetLightmapSize(Vector2i extents) =>
				(extents / c_lightmapScale) + new Vector2i(1, 1);

			public bool Advance(uint uMax, uint vMax, uint size, out uint u, out uint v)
			{
				uMax /= c_lightmapScale;
				vMax /= c_lightmapScale;
				uMax++;
				vMax++;
				Debug.Assert(uMax > 0);
				Debug.Assert(vMax > 0);
				u = CurrentU;
				v = CurrentV;
				if (uMax >= size || vMax >= size) return false;
				// we're out of vertical space
				if (CurrentV + vMax + c_padding * 2 >= size) return false;
				// check if we should go to the next row
				if (CurrentU + uMax + c_padding * 2 >= size)
				{
					u = c_padding;
					v += RowHeight + c_padding;
					CurrentU = c_padding + uMax;
					CurrentV += RowHeight + c_padding;
					RowHeight = vMax + c_padding;
					// we're out of vertical space
					if (CurrentV + vMax + c_padding * 2 >= size) return false;
				}
				else
					CurrentU += uMax + c_padding;
				RowHeight = Math.Max(RowHeight, vMax + c_padding);
				return true;
			}
		}

		private readonly GraphicsDevice _gd;
		private readonly IArrayAllocator _allocator;
		private readonly List<(LightmapAllocData data, Texture target)> _lightmaps =
			new List<(LightmapAllocData data, Texture texture)>();

		public LightmapAllocator(GraphicsDevice gd, IArrayAllocator allocator)
		{
			(_gd, _allocator) = (gd, allocator);
			CreateNewBlock();
		}

		private bool AllocateInCurrentBlock(uint uMax, uint vMax, out uint u, out uint v, out Texture staging)
		{
			staging = null;
			u = v = uint.MaxValue;
			if (!_lightmaps.Any()) return false;
			var index = _lightmaps.Count - 1;
			var item = _lightmaps[index];
			var data = item.data;
			var result = data.Advance(uMax, vMax, BlockSize, out u, out v);
			if (result)
				staging = data.Staging;
			_lightmaps[index] = (data, item.target);
			return result;
		}

		private Texture CreateLightmapTexture(bool staging) =>
			_gd.ResourceFactory.CreateTexture(new TextureDescription(
				BlockSize, BlockSize, 1, 1, LightmapsPerFace,
				PixelFormat.B8_G8_R8_A8_UNorm,
				staging ? TextureUsage.Staging : TextureUsage.Sampled,
				TextureType.Texture2D
			));

		private void CreateNewBlock()
		{
			var block = new LightmapAllocData(CreateLightmapTexture(staging: true));
			var target = CreateLightmapTexture(staging: false);
			_lightmaps.Add((block, target));
		}

		public static bool ShouldHaveLightmap(LTextureInfo tex)
		{
			var f = tex.Flags;
			if (f.HasFlag(SurfaceFlags.NoDraw) ||
				f.HasFlag(SurfaceFlags.Sky) ||
				f.HasFlag(SurfaceFlags.Transparent33) ||
				f.HasFlag(SurfaceFlags.Transparent66) ||
				f.HasFlag(SurfaceFlags.Warp))
				return false;
			return true;
		}

		public void AllocateBlock(
			int numMaps,
			Span<byte> data,
			int offset,
			Vector2i extents,
			out Vector2 position,
			out Vector2 scale,
			out Texture texture)
		{
			var u = 0u;
			var v = 0u;
			if (AllocateInCurrentBlock((uint)extents.X, (uint)extents.Y, out u, out v, out Texture staging))
				goto process;

			// we don't do that because the renderer doesn't switch between lightmaps properly right now
			// CreateNewBlock();
			// if (AllocateInCurrentBlock((uint)extents.X, (uint)extents.Y, out u, out v, out staging))
			// goto process;

			throw new ArgumentOutOfRangeException("Unable to allocate lightmap block");

		process:
			position = new Vector2((float)u / BlockSize, (float)v / BlockSize);
			scale = new Vector2((float)(extents.X + 16) / c_coordScale, (float)(extents.Y + 16) / c_coordScale);
			// inset the coordinates for half of a pixel to avoid edge artifacts
			position += s_halfPixelVec;
			scale -= s_pixelVec;

			texture = _lightmaps.Last().target;
			var texSize = LightmapAllocData.GetLightmapSize(extents);
			var pixelCount = texSize.X * texSize.Y;
			var black = new ColorRGBA(0, 0, 0);
			for (var map = 0; map < LightmapsPerFace; map++)
			{
				var isBlack = map >= numMaps;
				Span<ColorRGBA> pixels = stackalloc ColorRGBA[pixelCount];
				for (var i = 0; i < pixelCount; i++)
					pixels[i] = isBlack ? black : new ColorRGBA(
						data[offset + i * 3 + 2],
						data[offset + i * 3 + 1],
						data[offset + i * 3]
					);
				var sizeInBytes = (uint)pixelCount * 4;
				unsafe
				{
					fixed (ColorRGBA* bp = pixels)
					{
						_gd.UpdateTexture(staging,
						(IntPtr)bp,
						sizeInBytes,
						u, v, 0,
						(uint)texSize.X, (uint)texSize.Y, 1,
						0, (uint)map);
					}
				}
				offset += pixelCount * 3;
			}
		}

		public void CompileLightmaps()
		{
			// TODO: Dispose staging textures
			var cl = _gd.ResourceFactory.CreateCommandList();
			cl.Begin();
			foreach (var (data, target) in _lightmaps)
				for (var i = 0; i < LightmapsPerFace; i++)
					cl.CopyTexture(data.Staging, target);
			cl.End();

			_gd.SubmitCommands(cl);
		}

	}
}