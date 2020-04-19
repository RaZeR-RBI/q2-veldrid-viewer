using System;
using System.IO;
using Common;
using static Common.Util;
using static System.Buffers.Binary.BinaryPrimitives;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

namespace MD2Viewer
{
	public class MD2File : IDisposable
	{
		public readonly Lump<MD2Skin> Skins;
		public readonly Lump<MD2Vertex> Vertexes;
		public readonly Lump<MD2TexCoord> TextureCoords;
		public readonly Lump<MD2Triangle> Triangles;
		public readonly Lump<LIntValue> Commands;

		private readonly MD2Vertex[] _frameBackingArray;
		private readonly MD2Frame[] _frames;
		private readonly int _frameCount;

		private readonly IArrayAllocator _allocator;


		private const int HeaderSize = 68;
		public MD2File(Stream stream, IArrayAllocator allocator)
		{
			_allocator = allocator;
			Span<byte> headerBytes = stackalloc byte[HeaderSize];
			if (stream.Read(headerBytes) < HeaderSize)
				throw new EndOfStreamException("Unexpected end of stream while reading header");
			if (headerBytes[0] != (byte)'I' ||
				headerBytes[1] != (byte)'D' ||
				headerBytes[2] != (byte)'P' ||
				headerBytes[3] != (byte)'2')
			{
				throw new IOException("Invalid MD2 file");
			}
			var version = ReadUInt32LittleEndian(headerBytes.Slice(4));
			if (version != 8)
				throw new NotSupportedException("Only MD2 version 8 is supported");

			var offset = 4;

			var skinWidth = ReadIntLE(headerBytes, ref offset);
			var skinHeight = ReadIntLE(headerBytes, ref offset);
			var frameSize = ReadIntLE(headerBytes, ref offset);

			var numSkins = ReadIntLE(headerBytes, ref offset);
			var numVerts = ReadIntLE(headerBytes, ref offset);
			var numTexCoords = ReadIntLE(headerBytes, ref offset);
			var numTris = ReadIntLE(headerBytes, ref offset);
			var numCmds = ReadIntLE(headerBytes, ref offset);
			_frameCount = ReadIntLE(headerBytes, ref offset);

			var offsetSkins = ReadIntLE(headerBytes, ref offset);
			var offsetVerts = ReadIntLE(headerBytes, ref offset);
			var offsetTexCoords = ReadIntLE(headerBytes, ref offset);
			var offsetTris = ReadIntLE(headerBytes, ref offset);
			var offsetCmds = ReadIntLE(headerBytes, ref offset);
			var offsetFrames = ReadIntLE(headerBytes, ref offset);

			Skins = new Lump<MD2Skin>(allocator);
			Vertexes = new Lump<MD2Vertex>(allocator);
			TextureCoords = new Lump<MD2TexCoord>(allocator);
			Triangles = new Lump<MD2Triangle>(allocator);
			Commands = new Lump<LIntValue>(allocator);

			Skins.Read(stream, offsetSkins, numSkins * default(MD2Skin).Size);
			Vertexes.Read(stream, offsetVerts, numVerts * default(MD2Vertex).Size);
			TextureCoords.Read(stream, offsetTexCoords, numTexCoords * default(MD2TexCoord).Size);
			Triangles.Read(stream, offsetTris, numTris * default(MD2Triangle).Size);
			Commands.Read(stream, offsetCmds, numCmds * sizeof(int));

			// read frames
			_frames = _allocator.Rent<MD2Frame>(_frameCount);
			_frameBackingArray = _allocator.Rent<MD2Vertex>(_frameCount * numVerts);
			// two vectors, 16 chars of text and numVerts of struct MD2Vertex
			if (frameSize < 3 * 2 + 16 + default(MD2Vertex).Size * numVerts)
				throw new IOException("Invalid frame size specified");
			var ms = new MemoryStream(frameSize);
			stream.Seek(offsetFrames, SeekOrigin.Begin);
			for (var i = 0; i < _frameCount; i++)
			{
				var frame = new MD2Frame();
				frame.Vertices = new Memory<MD2Vertex>(_frameBackingArray, i * numVerts, numVerts);

				ms.Seek(0, SeekOrigin.Begin);
				ChunkedStreamRead(stream, ms, frameSize);
				ms.Seek(0, SeekOrigin.Begin);

				frame.Scale = ReadVector3(ms);
				frame.Translate = ReadVector3(ms);

				Span<byte> name = stackalloc byte[16];
				if (ms.Read(name) != name.Length)
					throw new EndOfStreamException("Unexpected end of stream");
				frame.Name = ReadNullTerminated(name);

				for (var j = 0; j < numVerts; j++)
				{
					Span<byte> vertData = stackalloc byte[4];
					if (ms.Read(vertData) != vertData.Length)
						throw new EndOfStreamException("Unexpected end of stream");
					var vertex = new MD2Vertex();
					vertex.Read(vertData);
					_frameBackingArray[i * numVerts + j] = vertex;
				}
			}
			ms.Dispose();
		}

		private int ReadIntLE(ReadOnlySpan<byte> bytes, ref int offset)
		{
			offset += 4;
			return ReadInt32LittleEndian(bytes.Slice(offset));
		}

		private bool _isDisposed = false;
		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			Skins.Dispose();
			Vertexes.Dispose();
			TextureCoords.Dispose();
			Triangles.Dispose();
			Commands.Dispose();
		}
	}
}