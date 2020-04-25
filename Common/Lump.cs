using System;
using System.Collections.Generic;
using System.IO;
using static Common.Util;

namespace Common
{
	public interface ILumpData
	{
		int Size { get; }
		void Read(ReadOnlySpan<byte> bytes);
	}

	public class Lump<T> : IDisposable where T : unmanaged, ILumpData
	{
		protected readonly IMemoryAllocator _allocator;

		protected DisposableArray<byte> _rawData = DisposableArray<byte>.Null;
		protected DisposableArray<T> _data = DisposableArray<T>.Null;

		public Span<T> Data => _data;
		public Span<byte> RawData => _rawData;

		public bool IsRawData { get; private set; }
		public int Length { get; private set; }

		public Lump(IMemoryAllocator allocator)
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
			_rawData = new DisposableArray<byte>(length, _allocator);
			Length = length;
			Span<byte> buffer = stackalloc byte[4096];
			var total = 0;
			var offset = 0;
			Span<byte> target = _rawData;
			while (true)
			{
				var bytesRead = stream.Read(buffer);
				total += bytesRead;
				if (total > length)
					bytesRead -= (total - length);
				var count = Math.Min(bytesRead, length);
				buffer.Slice(0, count).CopyTo(target.Slice(offset));
				offset += count;
				if (bytesRead < 4096) break;
			}
		}

		protected void ReadSerialized(Stream stream, int lengthInBytes)
		{
			var size = (new T()).Size;
			if (lengthInBytes % size != 0)
			{
				throw new IOException(
					$"Invalid lump entry (at {stream.Position}, size {lengthInBytes}), size must be a multiple of {size}"
				);
			}
			Length = lengthInBytes / size;
			Span<byte> buffer = stackalloc byte[size];
			_data = new DisposableArray<T>(Length, _allocator);
			Span<T> target = _data;
			for (int i = 0; i < Length; i++)
			{
				EnsureRead(stream, buffer);
				var item = new T();
				item.Read(buffer);
				target[i] = item;
			}
		}

		private bool _isDisposed = false;
		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			if (!_data.IsNull()) _data.Dispose();
			if (!_rawData.IsNull()) _rawData.Dispose();
			Length = 0;
		}
	}
}