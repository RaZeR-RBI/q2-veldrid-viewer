using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Veldrid;

namespace Q2Viewer
{
	public class BSPRenderer
	{
		private readonly BSPReader _reader;
		private readonly BSPFile _file;
		private IArrayAllocator _allocator;

		private List<(DeviceBuffer, uint)> _wireframes = new List<(DeviceBuffer, uint)>();

		public BSPRenderer(BSPFile file, IArrayAllocator allocator, GraphicsDevice gd)
		{
			_file = file;
			_reader = new BSPReader(file);
			_allocator = allocator;

			var worldspawnWireframe = BuildDebugEdgeBuffer(gd,
				_reader.GetModels().First(),
				RgbaFloat.White,
				out uint wsEdgeCount);

			_wireframes.Add((worldspawnWireframe, wsEdgeCount));
			foreach (var model in _reader.GetModels().Skip(1))
			{
				var wf = BuildDebugEdgeBuffer(gd,
				model, RgbaFloat.Red, out uint edgeCount);
				_wireframes.Add((wf, edgeCount));
			}
		}

		public DeviceBuffer BuildDebugEdgeBuffer(GraphicsDevice gd, LModel model, RgbaFloat color, out uint count)
		{
			count = 0;
			for (var i = model.FirstFace; i < model.FirstFace + model.NumFaces; i++)
				count += (uint)_file.Faces.Data[i].EdgeCount * 2;

			var vertexSize = VertexColor.SizeInBytes;
			var vertices = _allocator.Rent<VertexColor>((int)count);
			var buffer = gd.ResourceFactory.CreateBuffer(
				new BufferDescription(count * vertexSize, BufferUsage.VertexBuffer));

			var offset = 0;
			for (var i = model.FirstFace; i < model.FirstFace + model.NumFaces; i++)
			{
				ref var face = ref _file.Faces.Data[i];
				for (var j = face.FirstEdgeId; j < face.FirstEdgeId + face.EdgeCount; j++)
				{
					ref var surfEdge = ref _file.SurfaceEdges.Data[j];
					ref var edge = ref _file.Edges.Data[Math.Abs(surfEdge.Value)];
					ref var v1 = ref _file.Vertexes.Data[edge.VertexID1];
					ref var v2 = ref _file.Vertexes.Data[edge.VertexID2];
					vertices[offset] = new VertexColor(v1.Point, color);
					vertices[offset + 1] = new VertexColor(v2.Point, color);
					offset += 2;
				}
			}
			var sizeInBytes = count * vertexSize;
			var memory = new Memory<VertexColor>(vertices, 0, (int)count);
			using (var handle = memory.Pin())
			{
				unsafe
				{
					gd.UpdateBuffer(buffer, 0, (IntPtr)handle.Pointer, sizeInBytes);
				}
			}
			_allocator.Return(vertices);
			count /= 2;
			return buffer;
		}

		public void DrawWireframe(CommandList cl, DebugPrimitives helper)
		{
			foreach (var (buffer, count) in _wireframes)
				helper.DrawLines(cl, Matrix4x4.Identity, buffer, count);
		}
	}
}