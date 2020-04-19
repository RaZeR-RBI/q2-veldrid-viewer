using System.Buffers;

namespace Common
{
	public class SharedArrayPoolAllocator : IArrayAllocator
	{
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

		public T[] Rent<T>(int size) =>
			ArrayPool<T>.Shared.Rent(size);

		public void Return<T>(T[] array) =>
			ArrayPool<T>.Shared.Return(array);
	}
}