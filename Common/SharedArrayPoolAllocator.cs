using System.Buffers;
using System.Threading;

namespace Common
{
	public class SharedArrayPoolAllocator : IArrayAllocator
	{
		private volatile int _count = 0;
		private static SharedArrayPoolAllocator _instance = null;
		public static SharedArrayPoolAllocator Instance
		{
			get
			{
				if (_instance == null)
					_instance = new SharedArrayPoolAllocator();
				return _instance;
			}
		}

		private SharedArrayPoolAllocator() { }

		public T[] Rent<T>(int size)
		{
			var result = ArrayPool<T>.Shared.Rent(size);
			Interlocked.Increment(ref _count);
			return result;
		}

		public void Return<T>(T[] array)
		{
			ArrayPool<T>.Shared.Return(array);
			Interlocked.Decrement(ref _count);
		}

		public int GetActiveAllocationsCount() => _count;
	}
}