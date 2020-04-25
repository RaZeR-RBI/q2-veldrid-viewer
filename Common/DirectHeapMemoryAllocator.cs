using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Common
{
	public class DirectHeapMemoryAllocator : IMemoryAllocator
	{
		private static readonly DirectHeapMemoryAllocator s_instance = new DirectHeapMemoryAllocator();

		public static DirectHeapMemoryAllocator Instance => s_instance;

		private volatile int _activeAllocations;

		public unsafe T* Allocate<T>(int count) where T : unmanaged
		{
			var result = (T*)Marshal.AllocHGlobal(sizeof(T) * count);
			Interlocked.Increment(ref _activeAllocations);
			return result;
		}

		public unsafe void Free<T>(T* ptr) where T : unmanaged
		{
			Marshal.FreeHGlobal((IntPtr)ptr);
			Interlocked.Decrement(ref _activeAllocations);
		}

		public int GetActiveAllocationsCount() => _activeAllocations;
	}
}