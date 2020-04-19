namespace Common
{
	public interface IArrayAllocator
	{
		T[] Rent<T>(int size);
		void Return<T>(T[] array);
	}
}