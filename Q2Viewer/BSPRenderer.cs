using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using SharpFileSystem;
using Veldrid;

namespace Q2Viewer
{
	public struct TexturedFaceGroup
	{
		public SurfaceFlags Flags;
		public Texture Texture;
		public Texture Lightmap;
		public DeviceBuffer Buffer;
		public uint Count;
		public AABB Bounds;

		public void Dispose()
		{
			Texture?.Dispose();
			Lightmap?.Dispose();
			Buffer?.Dispose();
			Texture = Lightmap = null;
			Buffer = null;
			Count = 0;
		}
	}

	public struct ModelRenderInfo
	{
		public TexturedFaceGroup[] FaceGroups;
		public int FaceGroupsCount;

		public void Dispose(IArrayAllocator allocator)
		{
			if (FaceGroups == null) return;
			foreach (var fg in FaceGroups)
				fg.Dispose();
			allocator.Return(FaceGroups);
			FaceGroups = null;
			FaceGroupsCount = 0;
		}
	}

	public class BSPRenderer
	{
		private readonly BSPReader _reader;
		private readonly BSPFile _file;
		private readonly IArrayAllocator _allocator;
		private readonly LightmapAllocator _lm;

		private readonly List<(DeviceBuffer, uint)> _wireframes = new List<(DeviceBuffer, uint)>();
		private readonly List<(DeviceBuffer, uint)> _debugModels =
			new List<(DeviceBuffer, uint)>();
		private readonly List<ModelRenderInfo> _models = new List<ModelRenderInfo>();

		private readonly TexturePool _texPool;
		private readonly GraphicsDevice _gd;

		public BSPRenderer(BSPFile file, IArrayAllocator allocator, GraphicsDevice gd, IFileSystem fs)
		{
			_file = file;
			_reader = new BSPReader(file);
			_allocator = allocator;
			_gd = gd;
			_lm = new LightmapAllocator(_gd, _allocator);

			_texPool = new TexturePool(gd, file, fs, allocator);

			foreach (var model in _reader.GetModels())
			{
				var mri = BuildModelRenderInfo(model);
				if (mri.FaceGroupsCount > 0)
					_models.Add(mri);

				// debugging stuff
				var mb = BuildDebugModelBuffers(gd, model, out uint count);
				if (mb != null)
				{
					_debugModels.Add((mb, count));
					// continue;
				}
				// we got an invisible model - a trigger or something
				var wf = BuildDebugEdgeBuffer(gd,
				model, RgbaFloat.Blue, out uint edgeCount);
				_wireframes.Add((wf, edgeCount));
			}
		}

		private bool IsDrawable(SurfaceFlags flags) =>
			!flags.HasFlag(SurfaceFlags.NoDraw) && !flags.HasFlag(SurfaceFlags.Sky);

		private ModelRenderInfo BuildModelRenderInfo(LModel model)
		{
			var fFirstIndex = model.FirstFace;
			var fCount = model.NumFaces;
			// TODO: Think about doing this without LINQ
			var grouping = Enumerable.Range(fFirstIndex, fCount)
				.Where(i =>
				{
					ref var face = ref _file.Faces.Data[i];
					ref var tex = ref _file.TextureInfos.Data[face.TextureInfoId];
					return IsDrawable(tex.Flags);
				})
				.GroupBy(i =>
				{
					ref var face = ref _file.Faces.Data[i];
					ref var tex = ref _file.TextureInfos.Data[face.TextureInfoId];
					return (TextureName: tex.TextureName.ToLowerInvariant(), tex.Flags);
				});

			// TODO: Build lightmaps
			var mri = new ModelRenderInfo();
			mri.FaceGroupsCount = grouping.Count();
			mri.FaceGroups = _allocator.Rent<TexturedFaceGroup>(mri.FaceGroupsCount);
			var tfgId = 0;

			foreach (var group in grouping)
			{
				var tfg = new TexturedFaceGroup();
				var textureName = group.Key.TextureName;
				tfg.Texture = _texPool.GetTexture(textureName);
				var count = (uint)group.Select(id =>
					BSPReader.GetFaceVertexCount(_file.Faces.Data[id])).Sum();
				tfg.Flags = group.Key.Flags;
				tfg.Count = count;

				var vertices = _allocator.Rent<VertexNTL>((int)count);
				var offset = 0;
				_reader.ProcessVertices(group, (faceIndex, f, ev, tMin, tExt) =>
				{

					var triangleCount = BSPReader.GetFaceTriangleCount(ev);
					Span<Entry<VertexNTL>> vt = stackalloc Entry<VertexNTL>[triangleCount * 3];
					BSPReader.Triangulate(ev, vt);
					foreach (var entry in vt)
					{
						var vertex = entry.Value;
						vertex.UV.X /= tfg.Texture.Width;
						vertex.UV.Y /= tfg.Texture.Height;
						// TODO: Adjust the lightmap coordinates
						vertices[offset] = vertex;
						offset++;
					}
					ref var texInfo = ref _file.TextureInfos.Data[f.TextureInfoId];
					var hasLightmap = LightmapAllocator.ShouldHaveLightmap(texInfo);

					if (hasLightmap)
					{
						_lm.AllocateBlock(_file.Lighting.RawData, f.LightOffset, tExt,
							out Vector2 lmPos, out Vector2 lmSize, out Texture lmTexture);
						Debug.Assert(vertices.Select(v => v.LightmapUV).Distinct().Count() > 1);
						for (var k = offset - vt.Length; k < offset; k++)
						{
							ref var v = ref vertices[k].LightmapUV;
							var normalizedUV = v;
							v.X = lmPos.X + normalizedUV.X * lmSize.X;
							v.Y = lmPos.Y + normalizedUV.Y * lmSize.Y;
						}
						// TODO: Check if it's a new lightmap and split into new face group if so
						tfg.Lightmap = lmTexture;
					}
					else tfg.Lightmap = null;
				});
				// calculate AABB
				var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
				var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
				for (var i = 0; i < count; i++)
				{
					ref var point = ref vertices[i].Position;
					min = Vector3.Min(point, min);
					max = Vector3.Max(point, max);
				}
				tfg.Bounds = new AABB(min, max);

				var buffer = _gd.ResourceFactory.CreateBuffer(
					new BufferDescription(count * VertexNTL.SizeInBytes, BufferUsage.VertexBuffer)
				);
				UpdateBuffer(buffer, vertices, count, VertexNTL.SizeInBytes);
				tfg.Buffer = buffer;
				mri.FaceGroups[tfgId] = tfg;
				tfgId++;
			}
			_lm.CompileLightmaps();
			return mri;
		}

		private DeviceBuffer BuildDebugEdgeBuffer(GraphicsDevice gd, LModel model, RgbaFloat color, out uint count)
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
			UpdateBuffer(buffer, vertices, count, vertexSize);
			_allocator.Return(vertices);
			return buffer;
		}

		private void UpdateBuffer<T>(DeviceBuffer buffer, T[] data, uint count, uint itemSize)
		{
			var memory = new Memory<T>(data, 0, (int)count);
			using (var handle = memory.Pin())
				unsafe
				{
					_gd.UpdateBuffer(buffer, 0, (IntPtr)handle.Pointer, count * itemSize);
				}
		}

		public DeviceBuffer BuildDebugModelBuffers(
			GraphicsDevice gd,
			LModel model,
			out uint count)
		{
			count = 0;
			for (var i = model.FirstFace; i < model.FirstFace + model.NumFaces; i++)
				// count += (uint)BSPReader.GetFaceVertexCount(_file.Faces.Data[i]);
				count += 3 + ((uint)_file.Faces.Data[i].EdgeCount - 3) * 3;

			var vertexSize = VertexColor.SizeInBytes;
			var vertices = _allocator.Rent<VertexColor>((int)count);
			var memory = new Memory<VertexColor>(vertices, 0, (int)count);

			var offset = 0;
			_reader.ProcessVertices(model, (_, f, v, __, ___) =>
			{
				ref var texInfo = ref _file.TextureInfos.Data[f.TextureInfoId];
				// skip triggers, clips and other invisible faces
				if (!IsDrawable(texInfo.Flags))
					return;
				var triangleCount = BSPReader.GetFaceTriangleCount(v);
				Span<Entry<VertexNTL>> vt = stackalloc Entry<VertexNTL>[triangleCount * 3];
				BSPReader.Triangulate(v, vt);
				Span<VertexColor> vc = stackalloc VertexColor[triangleCount * 3];
				var color = Util.GetRandomColor();
				for (var i = 0; i < vt.Length; i++)
					vc[i] = new VertexColor(vt[i].Value.Position, color);
				vc.CopyTo(memory.Slice(offset, triangleCount * 3).Span);
				offset += vc.Length;
			});
			count = (uint)offset;
			if (count == 0)
			{
				_allocator.Return(vertices);
				return null;
			}

			var buffer = gd.ResourceFactory.CreateBuffer(
				new BufferDescription(count * vertexSize, BufferUsage.VertexBuffer));
			UpdateBuffer(buffer, vertices, count, vertexSize);
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
			foreach (var (vb, count) in _debugModels)
				helper.DrawTriangles(cl, Matrix4x4.Identity, vb, count);
			foreach (var (vb, count) in _wireframes)
				helper.DrawLines(cl, Matrix4x4.Identity, vb, count);
		}

		public void DebugDrawFrustumCheck(CommandList cl, DebugPrimitives helper)
		{
			var matVp = helper.Camera.ViewMatrix * helper.Camera.ProjectionMatrix;
			foreach (var mri in _models)
				for (var i = 0; i < mri.FaceGroupsCount; i++)
				{
					var aabb = mri.FaceGroups[i].Bounds;
					helper.DrawAABB(cl, aabb, Util.CheckIfOutside(matVp, aabb));
				}
		}

		public int Draw(CommandList cl, ModelRenderer renderer)
		{
			var calls = 0;
			foreach (var mri in _models)
				calls += renderer.Draw(cl, mri, Matrix4x4.Identity);
			return calls;
		}
	}
}