using System;

namespace Common
{
	public unsafe interface IMemoryAllocator
	{
		T* Allocate<T>(int count) where T : unmanaged;
		void Free<T>(T* ptr) where T : unmanaged;

		int GetActiveAllocationsCount();
	}
}