using System;
using System.Runtime.InteropServices;
using Veldrid;

namespace Common
{
	public static class GraphicsDeviceExtensions
	{
		public static void UpdateBuffer<T>(this GraphicsDevice gd, DeviceBuffer buffer, T[] data, uint count, uint itemSize, uint offset = 0)
		{
			// TODO: Investigate if there is a better way to do that
			var memory = new Memory<T>(data, 0, (int)count);
			using (var handle = memory.Pin())
				unsafe
				{
					gd.UpdateBuffer(buffer, offset * itemSize, (IntPtr)handle.Pointer, count * itemSize);
				}
		}

		public static void UpdateBuffer<T>(this GraphicsDevice gd, DeviceBuffer buffer, Span<T> data)
			where T : unmanaged
		=> gd.UpdateBuffer(buffer, (ReadOnlySpan<T>)data);

		public static void UpdateBuffer<T>(this GraphicsDevice gd, DeviceBuffer buffer, ReadOnlySpan<T> data)
			where T : unmanaged
		{
			unsafe
			{
				fixed (T* p = &data.GetPinnableReference())
				{
					gd.UpdateBuffer(buffer, 0, (IntPtr)p, (uint)sizeof(T) * (uint)data.Length);
				}
			}
		}

		public unsafe static void UpdateTexture<T>(this GraphicsDevice gd, Texture texture, ReadOnlySpan<T> data, uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer)
			where T : unmanaged
		{
			uint sizeInBytes = (uint)(sizeof(T) * data.Length);
			fixed (T* ptr = &data.GetPinnableReference())
			{
				gd.UpdateTexture(texture, (IntPtr)ptr, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
			}
		}
	}
}