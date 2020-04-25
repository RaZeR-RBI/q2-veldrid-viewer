using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Common;
using static Common.Util;
using static System.Buffers.Binary.BinaryPrimitives;

namespace Q2Viewer
{
	public class BSPFile : IDisposable
	{
		private const int HeaderSize = 152;

		public readonly Lump<LRawValue> Entities; // LUMP_ENTITIES = 0
		public readonly Lump<LPlane> Planes; // LUMP_PLANES = 1
		public readonly Lump<LVertexPosition> Vertexes; // LUMP_VERTEXES = 2
		public readonly Lump<LRawValue> Visibility; // LUMP_VISIBILITY = 3
		public readonly Lump<LNode> Nodes; // LUMP_NODES = 4
		public readonly Lump<LTextureInfo> TextureInfos; // LUMP_TEXINFO = 5
		public readonly Lump<LFace> Faces; // LUMP_FACES = 6
		public readonly Lump<LRawValue> Lighting; // LUMP_LIGHTING = 7
		public readonly Lump<LLeaf> Leaves; // LUMP_LEAFS = 8
		public readonly Lump<LShortValue> LeafFaces; // LUMP_LEAFFACES = 9
		public readonly Lump<LShortValue> LeafBrushes; // LUMP_LEAFBRUSHES = 10
		public readonly Lump<LEdge> Edges; // LUMP_EDGES = 11
		public readonly Lump<LIntValue> SurfaceEdges; // LUMP_SURFEDGES = 12
		public readonly Lump<LModel> Submodels; // LUMP_MODELS = 13
		public readonly Lump<LBrush> Brushes; // LUMP_BRUSHES = 14
		public readonly Lump<LBrushSide> BrushSides; // LUMP_BRUSHSIDES = 15
													 // LUMP_POP = 16 is unused
		public readonly Lump<LArea> Areas; // LUMP_AREAS = 17
		public readonly Lump<LAreaPortal> AreaPortals; // LUMP_AREAS = 18

		public readonly string EntitiesString;

		public BSPFile(Stream stream, IMemoryAllocator allocator)
		{
			Span<byte> headerBytes = stackalloc byte[HeaderSize];
			EnsureRead(stream, headerBytes);
			if (headerBytes[0] != (byte)'I' ||
				headerBytes[1] != (byte)'B' ||
				headerBytes[2] != (byte)'S' ||
				headerBytes[3] != (byte)'P')
			{
				throw new IOException("Invalid BSP file");
			}
			var version = ReadUInt32LittleEndian(headerBytes.Slice(4));
			if (version != 38)
				throw new NotSupportedException("Only BSP version 38 is supported");

			Entities = new Lump<LRawValue>(allocator);
			Planes = new Lump<LPlane>(allocator);
			Vertexes = new Lump<LVertexPosition>(allocator);
			Visibility = new Lump<LRawValue>(allocator);
			Nodes = new Lump<LNode>(allocator);
			TextureInfos = new Lump<LTextureInfo>(allocator);
			Faces = new Lump<LFace>(allocator);
			Lighting = new Lump<LRawValue>(allocator);
			Leaves = new Lump<LLeaf>(allocator);
			LeafFaces = new Lump<LShortValue>(allocator);
			LeafBrushes = new Lump<LShortValue>(allocator);
			Edges = new Lump<LEdge>(allocator);
			SurfaceEdges = new Lump<LIntValue>(allocator);
			Submodels = new Lump<LModel>(allocator);
			Brushes = new Lump<LBrush>(allocator);
			BrushSides = new Lump<LBrushSide>(allocator);
			Areas = new Lump<LArea>(allocator);
			AreaPortals = new Lump<LAreaPortal>(allocator);

			ReadData(headerBytes.Slice(8), stream);
			EntitiesString = Encoding.UTF8.GetString(Entities.RawData);
		}

		private void ReadData(ReadOnlySpan<byte> lumps, Stream stream)
		{
			List<Action<int, int>> readers = new List<Action<int, int>> {
				(o, l) => Entities.Read(stream, o, l),
				(o, l) => Planes.Read(stream, o, l),
				(o, l) => Vertexes.Read(stream, o, l),
				(o, l) => Visibility.Read(stream, o, l),
				(o, l) => Nodes.Read(stream, o, l),
				(o, l) => TextureInfos.Read(stream, o, l),
				(o, l) => Faces.Read(stream, o, l),
				(o, l) => Lighting.Read(stream, o, l),
				(o, l) => Leaves.Read(stream, o, l),
				(o, l) => LeafFaces.Read(stream, o, l),
				(o, l) => LeafBrushes.Read(stream, o, l),
				(o, l) => Edges.Read(stream, o, l),
				(o, l) => SurfaceEdges.Read(stream, o, l),
				(o, l) => Submodels.Read(stream, o, l),
				(o, l) => Brushes.Read(stream, o, l),
				(o, l) => BrushSides.Read(stream, o, l),
				(o, l) => {},
				(o, l) => Areas.Read(stream, o, l),
				(o, l) => AreaPortals.Read(stream, o, l)
			};
			for (int i = 0; i < 18; i++)
			{
				var offset = ReadInt32LittleEndian(lumps.Slice(i * 8));
				var length = ReadInt32LittleEndian(lumps.Slice(i * 8 + 4));
				var reader = readers[i];
				reader(offset, length);
			}
		}

		private bool _isDisposed = false;
		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			Entities.Dispose();
			Planes.Dispose();
			Vertexes.Dispose();
			Visibility.Dispose();
			Nodes.Dispose();
			TextureInfos.Dispose();
			Faces.Dispose();
			Lighting.Dispose();
			Leaves.Dispose();
			LeafFaces.Dispose();
			LeafBrushes.Dispose();
			Edges.Dispose();
			SurfaceEdges.Dispose();
			Submodels.Dispose();
			Brushes.Dispose();
			BrushSides.Dispose();
			Areas.Dispose();
			AreaPortals.Dispose();
		}
	}
}