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
		private List<(DeviceBuffer, uint)> _debugModels =
			new List<(DeviceBuffer, uint)>();

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

			foreach (var model in _reader.GetModels())
			{
				var mb = BuildDebugModelBuffers(gd, model, out uint count);
				_debugModels.Add((mb, count));
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
				unsafe
				{
					gd.UpdateBuffer(buffer, 0, (IntPtr)handle.Pointer, sizeInBytes);
				}
			_allocator.Return(vertices);
			return buffer;
		}

		public DeviceBuffer BuildDebugModelBuffers(
			GraphicsDevice gd,
			LModel model,
			out uint count)
		{
			count = 0;
			for (var i = model.FirstFace; i < model.FirstFace + model.NumFaces; i++)
				count += 3 + ((uint)_file.Faces.Data[i].EdgeCount - 3) * 3;

			var vertexSize = VertexColor.SizeInBytes;
			var vertices = _allocator.Rent<VertexColor>((int)count);
			var buffer = gd.ResourceFactory.CreateBuffer(
				new BufferDescription(count * vertexSize, BufferUsage.VertexBuffer));
			var memory = new Memory<VertexColor>(vertices, 0, (int)count);

			var offset = 0;
			_reader.ProcessVertices(model, (f, v) =>
			{
				ref var texInfo = ref _file.TextureInfos.Data[f.TextureInfoId];
				// skip triggers, clips and other invisible faces
				if (texInfo.Flags.HasFlag(SurfaceFlags.NoDraw) ||
					texInfo.Flags.HasFlag(SurfaceFlags.Sky))
					return;
				var triangleCount = BSPReader.GetFaceTriangleCount(v);
				Span<Entry<VertexTL>> vt = stackalloc Entry<VertexTL>[triangleCount * 3];
				BSPReader.Triangulate(v, vt);
				Span<VertexColor> vc = stackalloc VertexColor[triangleCount * 3];
				var color = Util.GetRandomColor();
				for (var i = 0; i < vt.Length; i++)
					vc[i] = new VertexColor(vt[i].Value.Position, color);
				vc.CopyTo(memory.Slice(offset, triangleCount * 3).Span);
				offset += vc.Length;
			});

			var sizeInBytes = count * vertexSize;
			using (var handle = memory.Pin())
				unsafe
				{
					gd.UpdateBuffer(buffer, 0, (IntPtr)handle.Pointer, sizeInBytes);
				}
			_allocator.Return(vertices);
			return buffer;
		}

		public void DrawWireframe(CommandList cl, DebugPrimitives helper)
		{
			foreach (var (buffer, count) in _wireframes)
				helper.DrawLines(cl, Matrix4x4.Identity, buffer, count);
		}

		public void DrawDebugModels(CommandList cl, DebugPrimitives helper)
		{
			foreach (var (vb, count) in _debugModels)
				helper.DrawTriangles(cl, Matrix4x4.Identity, vb, count);
		}

		public void DebugDraw(CommandList cl, DebugPrimitives helper)
		{
			var worldspawn = _debugModels.First();
			helper.DrawTriangles(cl, Matrix4x4.Identity, worldspawn.Item1, worldspawn.Item2);
			foreach (var (vb, count) in _wireframes.Skip(1))
				helper.DrawLines(cl, Matrix4x4.Identity, vb, count);
		}
	}
}