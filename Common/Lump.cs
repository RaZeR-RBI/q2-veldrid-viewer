using System;
using System.Collections.Generic;
using System.IO;

namespace Common
{
	public interface ILumpData
	{
		int Size { get; }
		void Read(ReadOnlySpan<byte> bytes);
	}

	public class Lump<T> : IDisposable where T : ILumpData, new()
	{
		protected readonly IArrayAllocator _allocator;

		protected byte[] _rawData = null;
		protected T[] _data = null;

		public T[] Data => _data;
		public byte[] RawData => _rawData;

		public bool IsRawData { get; private set; }
		public int Length { get; private set; }

		public Lump(IArrayAllocator allocator)
		{
			_allocator = allocator;
			IsRawData = (new T()).Size < 1;
		}

		public void Read(Stream stream, int offset, int length)
		{
			if (!stream.CanRead || !stream.CanSeek)
				throw new ArgumentException("Supplied stream must be seekable and readable", nameof(stream));

			if (length == 0) return;

			stream.Seek(offset, SeekOrigin.Begin);
			if (IsRawData)
				ReadRaw(stream, length);
			else
				ReadSerialized(stream, length);
		}

		protected void ReadRaw(Stream stream, int length)
		{
			_rawData = _allocator.Rent<byte>(length);
			Length = length;
			var ms = new MemoryStream(_rawData);
			Span<byte> buffer = stackalloc byte[4096];
			var total = 0;
			while (true)
			{
				var bytesRead = stream.Read(buffer);
				total += bytesRead;
				if (total > length)
					bytesRead -= (total - length);
				ms.Write(buffer.Slice(0, Math.Min(bytesRead, length)));
				if (bytesRead < 4096) break;
			}
			ms.Dispose();
		}

		protected void ReadSerialized(Stream stream, int lengthInBytes)
		{
			var size = (new T()).Size;
			if (lengthInBytes % size != 0)
			{
				throw new IOException(string.Format(
					"Invalid lump entry (at {}, size {}), size must be a multiple of {}",
					stream.Position, lengthInBytes, size));
			}
			Length = lengthInBytes / size;
			Span<byte> buffer = stackalloc byte[size];
			_data = _allocator.Rent<T>(Length);
			for (int i = 0; i < Length; i++)
			{
				if (stream.Read(buffer) < size)
					throw new EndOfStreamException("Unexpected end of stream while reading lump");
				var item = new T();
				item.Read(buffer);
				_data[i] = item;
			}
		}

		private bool _isDisposed = false;
		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			if (_data != null)
			{
				_allocator.Return(_data);
				_data = null;
			}
			if (_rawData != null)
			{
				_allocator.Return(_rawData);
				_rawData = null;
			}
			Length = 0;
		}
	}
}