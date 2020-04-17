using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using SharpFileSystem;
using Veldrid;
using static Imagini.Logger;
using static Common.Util;
using Common;

namespace Q2Viewer
{
	public struct TexturedFaceGroup
	{
		public SurfaceFlags Flags;
		public Texture Texture;
		public Texture Lightmap;
		public uint BufferOffset;
		public uint Count;
		public AABB Bounds;

		public byte LightmapStyle1;
		public byte LightmapStyle2;
		public byte LightmapStyle3;
		public byte LightmapStyle4;

		public void SetLightmapStyles(int styles)
		{
			LightmapStyle1 = (byte)(styles & 0x000000FF);
			LightmapStyle2 = (byte)((styles >> 8) & 0x000000FF);
			LightmapStyle3 = (byte)((styles >> 16) & 0x000000FF);
			LightmapStyle4 = (byte)((styles >> 24) & 0x000000FF);
		}

		public int GetLightmapCount()
		{
			if (LightmapStyle4 != 255) return 4;
			if (LightmapStyle3 != 255) return 3;
			if (LightmapStyle2 != 255) return 2;
			if (LightmapStyle1 != 255) return 1;
			return 0;
		}


		public void Dispose()
		{
			Texture?.Dispose();
			Lightmap?.Dispose();
			BufferOffset = 0;
			Texture = Lightmap = null;
			Count = 0;
		}
	}

	public struct ModelRenderInfo
	{
		public TexturedFaceGroup[] FaceGroups;
		public int FaceGroupsCount;
		public DeviceBuffer Buffer;

		public void Dispose(IArrayAllocator allocator)
		{
			if (FaceGroups == null) return;
			foreach (var fg in FaceGroups)
				fg.Dispose();
			allocator.Return(FaceGroups);
			FaceGroups = null;
			FaceGroupsCount = 0;
			Buffer?.Dispose();
			Buffer = null;
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
			var sw = new Stopwatch();
			_file = file;
			_reader = new BSPReader(file);
			_allocator = allocator;
			_gd = gd;
			_lm = new LightmapAllocator(_gd, _allocator);

			sw.Start();
			_texPool = new TexturePool(gd, file, fs, allocator);
			sw.Stop();
			Log.Debug($"Textures loaded in {FormatSW(sw)}");

			var modelIndex = 0;
			foreach (var model in _reader.GetModels())
			{
				sw.Restart();
				var mri = BuildModelRenderInfo(model);
				sw.Stop();
				Log.Debug($"Model #{modelIndex}: Created {mri.FaceGroupsCount} face groups in {FormatSW(sw)}");

				if (mri.FaceGroupsCount > 0)
				{
					_models.Add(mri);
				}

				// debugging stuff
				sw.Restart();
				var mb = BuildDebugModelBuffers(gd, model, out uint count);
				sw.Stop();
				Log.Debug($"Model #{modelIndex}: Created debug model in {FormatSW(sw)}");
				if (mb != null)
				{
					_debugModels.Add((mb, count));
				}

				sw.Restart();
				var wf = BuildDebugEdgeBuffer(
					gd,
					model,
					mri.FaceGroupsCount > 0 ? RgbaFloat.Red : RgbaFloat.Blue,
					out uint edgeCount);
				sw.Stop();
				Log.Debug($"Model #{modelIndex}: Created wireframe in {FormatSW(sw)}");

				_wireframes.Add((wf, edgeCount));
				modelIndex++;
			}
			Log.Debug($"Total models: {_models.Count}");
		}

		private bool IsDrawable(SurfaceFlags flags) =>
			!flags.HasFlag(SurfaceFlags.NoDraw) && !flags.HasFlag(SurfaceFlags.Sky);

		private ModelRenderInfo BuildModelRenderInfo(LModel model)
		{
			var fFirstIndex = model.FirstFace;
			var fCount = model.NumFaces;

			// TODO: Think about doing this without LINQ
			var totalVertices = Enumerable.Range(fFirstIndex, fCount)
				.Select(i => BSPReader.GetFaceVertexCount(_file.Faces.Data[i]))
				.Sum();

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
					return (
						TextureName: tex.TextureName.ToLowerInvariant(),
						Flags: tex.Flags,
						LightmapStyles: face.LightmapStyles);
				});

			// TODO: Build lightmaps
			var mri = new ModelRenderInfo();
			mri.FaceGroupsCount = grouping.Count();
			mri.FaceGroups = _allocator.Rent<TexturedFaceGroup>(mri.FaceGroupsCount);
			mri.Buffer = _gd.ResourceFactory.CreateBuffer(
				new BufferDescription((uint)totalVertices * VertexNTL.SizeInBytes, BufferUsage.VertexBuffer)
			);
			var tfgId = 0;
			var bufferOffset = 0u;

			foreach (var group in grouping)
			{
				var tfg = new TexturedFaceGroup();
				var textureName = group.Key.TextureName;
				tfg.Texture = _texPool.GetTexture(textureName);
				var count = (uint)group.Select(id =>
					BSPReader.GetFaceVertexCount(_file.Faces.Data[id])).Sum();
				tfg.Flags = group.Key.Flags;
				tfg.Count = count;
				tfg.BufferOffset = bufferOffset;
				tfg.SetLightmapStyles(group.Key.LightmapStyles);

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
						var numMaps = tfg.GetLightmapCount();
						_lm.AllocateBlock(numMaps, _file.Lighting.RawData, f.LightOffset, tExt,
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

				_gd.UpdateBuffer(mri.Buffer, vertices, count, VertexNTL.SizeInBytes, bufferOffset);
				mri.FaceGroups[tfgId] = tfg;
				tfgId++;
				bufferOffset += count;
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
			_gd.UpdateBuffer(buffer, vertices, count, vertexSize);
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
			gd.UpdateBuffer(buffer, vertices, count, vertexSize);
			_allocator.Return(vertices);
			return buffer;
		}

		public int DrawWireframe(CommandList cl, DebugPrimitives helper)
		{
			var calls = 0;
			foreach (var (buffer, count) in _wireframes)
			{
				helper.DrawLines(cl, Matrix4x4.Identity, buffer, count);
				calls++;
			}
			return calls;
		}

		public int DrawDebugModels(CommandList cl, DebugPrimitives helper)
		{
			var calls = 0;
			foreach (var (vb, count) in _debugModels)
			{
				helper.DrawTriangles(cl, Matrix4x4.Identity, vb, count);
				calls++;
			}
			return calls;
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