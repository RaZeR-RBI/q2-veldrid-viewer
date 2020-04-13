using System;
using Veldrid;

namespace Q2Viewer
{
	public static class GraphicsDeviceExtensions
	{
		public static void UpdateBuffer<T>(this GraphicsDevice gd, DeviceBuffer buffer, T[] data, uint count, uint itemSize)
		{
			// TODO: Investigate if there is a better way to do that
			var memory = new Memory<T>(data, 0, (int)count);
			using (var handle = memory.Pin())
				unsafe
				{
					gd.UpdateBuffer(buffer, 0, (IntPtr)handle.Pointer, count * itemSize);
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
	}
}