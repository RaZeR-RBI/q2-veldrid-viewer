using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Common;
using Veldrid;

namespace MD2Viewer
{
	public struct VATDescription
	{
		public float Time;
	}

	public static class VertexAnimationTexture
	{
		public static DisposableArray<VATDescription> CreateVAT(
			GraphicsDevice gd,
			MD2Reader reader,
			IMemoryAllocator allocator,
			out Texture positionTex,
			out Texture normalTex,
			out Vector3 translate,
			out Vector3 scale)
		{
			var model = reader.File;
			var frameCount = model.FrameCount;
			var result = new DisposableArray<VATDescription>(frameCount, allocator);
			var vertexCount = model.Triangles.Length * 3;

			var min = new Vector3(float.MaxValue);
			var max = new Vector3(float.MinValue);

			var ppix = new DisposableArray<ColorRGBA16>(vertexCount * frameCount, allocator);
			var npix = new DisposableArray<ColorRGBA>(vertexCount * frameCount, allocator);


			foreach (var frame in reader.GetFrames())
				reader.ProcessFrame(frame, (_, vertices) =>
				{
					for (var i = 0; i < vertexCount; i++)
					{
						min = Vector3.Min(min, vertices[i].Position);
						max = Vector3.Max(max, vertices[i].Position);
					}
				});

			translate = min;
			scale = max - min;

			var offset = 0;
			var vtrans = translate;
			var vscale = scale;

			var frameIndex = 0;
			var frameStep = 1f / (float)frameCount;
			foreach (var frame in reader.GetFrames())
			{
				reader.ProcessFrame(frame, (f, vertices) =>
				{
					for (var i = 0; i < vertexCount; i++)
					{
						var pNorm = (vertices[i].Position - vtrans) / vscale;
						var nNorm = (vertices[i].Normal + Vector3.One) / 2f;
						ppix[offset + i] = new ColorRGBA16(pNorm);
						npix[offset + i] = new ColorRGBA(nNorm);
						Debug.Assert(pNorm.X <= 1.0f);
						Debug.Assert(pNorm.Y <= 1.0f);
						Debug.Assert(pNorm.Z <= 1.0f);
						Debug.Assert(pNorm.X >= 0.0f);
						Debug.Assert(pNorm.Y >= 0.0f);
						Debug.Assert(pNorm.Z >= 0.0f);
					}
					result[frameIndex] = new VATDescription()
					{
						Time = frameIndex * frameStep,
					};
					offset += vertexCount;
					frameIndex++;
				});
			}
			positionTex = CreateTexture(gd, vertexCount, frameCount, ppix.AsReadOnlySpan(), PixelFormat.R16_G16_B16_A16_UNorm);
			normalTex = CreateTexture(gd, vertexCount, frameCount, npix.AsReadOnlySpan(), PixelFormat.R8_G8_B8_A8_UNorm);

			ppix.Dispose();
			npix.Dispose();
			return result;
		}

		private static Texture CreateTexture<T>(GraphicsDevice device, int width, int height, ReadOnlySpan<T> data, PixelFormat format)
			where T : unmanaged
		{
			var rf = device.ResourceFactory;
			var texture = rf.CreateTexture(new TextureDescription(
				(uint)width, (uint)height, 1, 1, 1, format, TextureUsage.Sampled, TextureType.Texture2D
			));
			device.UpdateTexture(texture, data, 0, 0, 0, (uint)width, (uint)height, 1, 0, 0);
			return texture;
		}
	}
}