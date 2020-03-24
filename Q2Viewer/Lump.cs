using System;

namespace Q2Viewer
{
	public interface ILumpData
	{
		int Size { get; }
		void Read(ReadOnlySpan<byte> bytes);
	}

	public class Lump<T> where T : ILumpData
	{

	}
}