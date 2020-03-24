using System;
using System.Collections.Generic;
using System.IO;
using SharpFileSystem;
using Microsoft.IO;
using static System.Buffers.Binary.BinaryPrimitives;
using System.Text;
using System.Linq;

namespace Q2Viewer
{
	public class QPakFS : IFileSystem
	{
		private struct FileEntry
		{
			public uint Offset;
			public uint Size;

			public FileEntry(uint offset, uint size) =>
				(this.Offset, this.Size) = (offset, size);
		}
		private const int HeaderSize = 64;
		private const int FileEntrySize = 64;
		private const int BufferSize = 4096;

		protected RecyclableMemoryStreamManager _sm = new RecyclableMemoryStreamManager(
			128 * 1024, // 128 KB
			1024 * 1024, // 1 MB
			8 * 1024 * 1024, // 8 MB,
			useExponentialLargeBuffer: true
		);

		protected readonly Stream _baseStream;

		private Dictionary<FileSystemPath, FileEntry> _entries =
			new Dictionary<FileSystemPath, FileEntry>();

		private readonly Dictionary<FileSystemPath, Stream> _streams =
			new Dictionary<FileSystemPath, Stream>();

		public QPakFS(Stream stream)
		{
			if (!stream.CanRead || !stream.CanSeek)
			{
				throw new ArgumentException("Supplied stream must be seekable and readable", nameof(stream));
			}
			_baseStream = stream;
			ReadHeader();
		}

		private void OnStreamDisposed(object sender, EventArgs e)
		{
			var s = sender as RecyclableMemoryStream;
			var items = _streams.Where(kvp => kvp.Value == s);
			foreach (var pair in items)
				_streams.Remove(pair.Key);
		}

		private void ReadHeader()
		{
			Span<byte> headerBytes = stackalloc byte[HeaderSize];
			_baseStream.Seek(0, SeekOrigin.Begin);
			_baseStream.Read(headerBytes);
			if (_baseStream.Position != HeaderSize)
			{
				throw new IOException("Unable to read enough bytes for a header");
			}
			if (headerBytes[0] != (byte)'P' ||
				headerBytes[1] != (byte)'A' ||
				headerBytes[2] != (byte)'C' ||
				headerBytes[3] != (byte)'K')
			{
				throw new IOException("Invalid PACK file");
			}

			uint tableOffset = ReadUInt32LittleEndian(headerBytes.Slice(4));
			uint tableSize = ReadUInt32LittleEndian(headerBytes.Slice(8));
			if (tableSize % FileEntrySize != 0)
			{
				throw new IOException("Invalid file entry table size");
			}

			uint fileCount = tableSize / FileEntrySize;
			Span<byte> entryBytes = stackalloc byte[FileEntrySize];
			_baseStream.Seek(tableOffset, SeekOrigin.Begin);
			for (uint i = 0; i < fileCount; i++)
			{
				_baseStream.Read(entryBytes);
				int length = 56;
				for (int j = 0; j < 56; j++)
				{
					if (entryBytes[j] == 0)
					{
						length = j; break;
					}
				}
				if (length == 0)
				{
					throw new IOException("Found an empty name in file table");
				}
				var name = Encoding.UTF8.GetString(entryBytes.Slice(0, length));
				var entryOffset = ReadUInt32LittleEndian(entryBytes.Slice(56));
				var entrySize = ReadUInt32LittleEndian(entryBytes.Slice(60));

				_entries.Add(FileSystemPath.Parse("/" + name),
							new FileEntry(entryOffset, entrySize));
			}
		}

		public Stream OpenFile(FileSystemPath path, FileAccess access)
		{
			CheckDisposed();
			if (access != FileAccess.Read) throw new NotSupportedException();
			if (_streams.ContainsKey(path)) throw new IOException("File is already opened");

			var entry = _entries[path];
			var newStream = _sm.GetStream(path.ToString(), (int)entry.Size);

			try
			{
				_baseStream.Seek(entry.Offset, SeekOrigin.Begin);
				Span<byte> buffer = stackalloc byte[BufferSize];
				var curOffset = 0;
				var end = (int)entry.Size;
				while (curOffset < end)
				{
					var bytesRead = _baseStream.Read(buffer);
					if (end - curOffset < BufferSize)
					{
						newStream.Write(buffer.Slice(0, end - curOffset));
						break;
					}
					else
					{
						if (bytesRead != BufferSize)
							throw new EndOfStreamException("Unexpected end of stream");
						newStream.Write(buffer);
					}
					curOffset += BufferSize;
				}
			}
			catch (Exception ex)
			{
				newStream.Dispose();
				throw new IOException(
					string.Format("Unable to read {} bytes starting from {}",
						entry.Offset, entry.Size), ex);
			}
			newStream.Seek(0, SeekOrigin.Begin);
			_streams.Add(path, newStream);
			return newStream;
		}

		public ICollection<FileSystemPath> GetEntities(FileSystemPath path)
		{
			CheckDisposed();
			if (path.IsRoot) return _entries.Keys;
			if (path.IsFile) return _entries.Keys.Where(p => p.Equals(path)).ToList();
			return _entries.Keys.Where(p => p.IsChildOf(path)).ToList();
		}

		private bool _isDisposed = false;
		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			var streamsToDispose = _streams.Values.ToList();
			foreach (var stream in streamsToDispose)
				stream.Dispose();
		}

		protected void CheckDisposed()
		{
			if (_isDisposed)
				throw new ObjectDisposedException(nameof(QPakFS));
		}

		public bool Exists(FileSystemPath path)
		{
			CheckDisposed();
			if (path.IsFile) return _entries.ContainsKey(path);
			return _entries.Keys.Any(p => p.Equals(path) || p.IsParentOf(path));
		}

		/* unused */
		public void CreateDirectory(FileSystemPath path)
		{
			CheckDisposed();
			throw new NotSupportedException();
		}

		public Stream CreateFile(FileSystemPath path)
		{
			CheckDisposed();
			throw new NotSupportedException();
		}

		public void Delete(FileSystemPath path)
		{
			CheckDisposed();
			throw new NotSupportedException();
		}
	}
}