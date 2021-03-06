using System;
using System.IO;
using Common;
using static Common.Util;
using static System.Buffers.Binary.BinaryPrimitives;

namespace MD2Viewer
{
	public class MD2File : IDisposable
	{
		public readonly int SkinWidth;
		public readonly int SkinHeight;
		public readonly int VertexCount;

		public readonly Lump<MD2Skin> Skins;
		public readonly Lump<MD2TexCoord> TextureCoords;
		public readonly Lump<MD2Triangle> Triangles;
		public readonly Lump<LIntValue> Commands;

		private readonly DisposableArray<MD2Vertex> _frameBackingArray;
		private readonly DisposableArray<MD2Frame> _frames;
		public int FrameCount { get; private set; }
		public Span<MD2Frame> Frames => _frames;

		private readonly IMemoryAllocator _allocator;


		private const int HeaderSize = 68;
		public MD2File(Stream stream, IMemoryAllocator allocator)
		{
			_allocator = allocator;
			Span<byte> headerBytes = stackalloc byte[HeaderSize];
			EnsureRead(stream, headerBytes);
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

			SkinWidth = ReadIntLE(headerBytes, ref offset);
			SkinHeight = ReadIntLE(headerBytes, ref offset);
			var frameSize = ReadIntLE(headerBytes, ref offset);

			var numSkins = ReadIntLE(headerBytes, ref offset);
			VertexCount = ReadIntLE(headerBytes, ref offset);
			var numTexCoords = ReadIntLE(headerBytes, ref offset);
			var numTris = ReadIntLE(headerBytes, ref offset);
			var numCmds = ReadIntLE(headerBytes, ref offset);
			FrameCount = ReadIntLE(headerBytes, ref offset);

			var offsetSkins = ReadIntLE(headerBytes, ref offset);
			var offsetTexCoords = ReadIntLE(headerBytes, ref offset);
			var offsetTris = ReadIntLE(headerBytes, ref offset);
			var offsetFrames = ReadIntLE(headerBytes, ref offset);
			var offsetCmds = ReadIntLE(headerBytes, ref offset);

			Skins = new Lump<MD2Skin>(allocator);
			TextureCoords = new Lump<MD2TexCoord>(allocator);
			Triangles = new Lump<MD2Triangle>(allocator);
			Commands = new Lump<LIntValue>(allocator);

			Skins.Read(stream, offsetSkins, numSkins * default(MD2Skin).Size);
			TextureCoords.Read(stream, offsetTexCoords, numTexCoords * default(MD2TexCoord).Size);
			Triangles.Read(stream, offsetTris, numTris * default(MD2Triangle).Size);
			Commands.Read(stream, offsetCmds, numCmds * sizeof(int));

			// read frames
			_frames = new DisposableArray<MD2Frame>(FrameCount, _allocator);
			_frameBackingArray = new DisposableArray<MD2Vertex>(FrameCount * VertexCount, _allocator);
			// two vectors, 16 chars of text and numVerts of struct MD2Vertex
			if (frameSize < 3 * 2 + 16 + default(MD2Vertex).Size * VertexCount)
				throw new IOException("Invalid frame size specified");
			var ms = new MemoryStream(frameSize);
			stream.Seek(offsetFrames, SeekOrigin.Begin);
			for (var i = 0; i < FrameCount; i++)
			{
				var frame = new MD2Frame();
				frame.StartVertex = i * VertexCount;

				Span<byte> frameData = stackalloc byte[frameSize];
				EnsureRead(stream, frameData);
				ms.Seek(0, SeekOrigin.Begin);
				ms.Write(frameData);
				ms.Seek(0, SeekOrigin.Begin);

				frame.Scale = ReadVector3XZY(ms);
				frame.Translate = ReadVector3XZY(ms);

				Span<byte> name = stackalloc byte[16];
				EnsureRead(ms, name);
				frame.SetName(name);

				for (var j = 0; j < VertexCount; j++)
				{
					Span<byte> vertData = stackalloc byte[4];
					EnsureRead(ms, vertData);
					var vertex = new MD2Vertex();
					vertex.Read(vertData);
					_frameBackingArray[i * VertexCount + j] = vertex;
				}
				_frames[i] = frame;
			}
			ms.Dispose();
		}

		public Span<MD2Vertex> GetVertices(int frameIndex) =>
			GetVertices(_frames[frameIndex]);

		public Span<MD2Vertex> GetVertices(MD2Frame frame) =>
			_frameBackingArray.AsSpan().Slice(frame.StartVertex, VertexCount);


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
			_frames.Dispose();
			_frameBackingArray.Dispose();
			Skins.Dispose();
			TextureCoords.Dispose();
			Triangles.Dispose();
			Commands.Dispose();
		}
	}
}