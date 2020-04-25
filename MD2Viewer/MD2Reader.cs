using System;
using System.Collections.Generic;
using System.Numerics;
using Common;

namespace MD2Viewer
{
	public delegate void MD2FrameCallback(string frameName, ReadOnlySpan<VertexNT> triangles);

	public class MD2Reader : IDisposable
	{
		public readonly MD2File File;
		private readonly IMemoryAllocator _allocator;
		public MD2Reader(MD2File file, IMemoryAllocator allocator) =>
			(File, _allocator) = (file, allocator);

		public void Dispose()
		{
			File.Dispose();
		}

		public Span<MD2Frame> GetFrames() => File.Frames;

		public void ProcessFrame(MD2Frame frame, MD2FrameCallback callback)
		{
			var totalVertices = File.Triangles.Length * 3;
			var backingArray = new DisposableArray<VertexNT>(totalVertices, _allocator);
			var vertices = File.GetVertices(frame);
			var texScale = new Vector2(File.SkinWidth, File.SkinHeight);
			for (var i = 0; i < File.Triangles.Length; i++)
			{
				var tri = File.Triangles.Data[i];

				var v1 = new VertexNT();
				var vf1 = vertices[tri.VertexID1];
				var tex1 = File.TextureCoords.Data[tri.TexCoordID1];
				v1.Position = vf1.GetPosition() * frame.Scale + frame.Translate;
				v1.UV = tex1.AsVector2() / texScale;
				v1.Normal = MD2Normals.Data[vf1.NormalIndex];

				var v2 = new VertexNT();
				var vf2 = vertices[tri.VertexID2];
				var tex2 = File.TextureCoords.Data[tri.TexCoordID2];
				v2.Position = vf2.GetPosition() * frame.Scale + frame.Translate;
				v2.UV = tex2.AsVector2() / texScale;
				v2.Normal = MD2Normals.Data[vf2.NormalIndex];

				var v3 = new VertexNT();
				var vf3 = vertices[tri.VertexID3];
				var tex3 = File.TextureCoords.Data[tri.TexCoordID3];
				v3.Position = vf3.GetPosition() * frame.Scale + frame.Translate;
				v3.UV = tex3.AsVector2() / texScale;
				v3.Normal = MD2Normals.Data[vf3.NormalIndex];

				backingArray[i * 3] = v1;
				backingArray[i * 3 + 1] = v2;
				backingArray[i * 3 + 2] = v3;
			}

			callback(frame.GetName(), backingArray.AsSpan().Slice(0, totalVertices));
			backingArray.Dispose();
		}
	}
}