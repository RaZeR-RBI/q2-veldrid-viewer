using System;
using System.Collections.Generic;
using System.Linq;

namespace Q2Viewer
{
	public delegate void FaceVisitorCallback(LFace data, Span<Entry<VertexTL>> vertices);

	public struct Entry<T>
	{
		public int Index;
		public T Value;
		public Entry(int index, T value) =>
			(this.Index, this.Value) = (index, value);
	}

	public class BSPReader
	{
		public readonly BSPFile File;
		public BSPReader(BSPFile file) => File = file;

		public IEnumerable<LModel> GetModels() =>
			File.Submodels.Data;

		public void ProcessVertices(LModel model, FaceVisitorCallback callback)
		{
			for (var i = model.FirstFace; i < model.FirstFace + model.NumFaces; i++)
			{
				var face = File.Faces.Data[i];
				Span<Entry<VertexTL>> vertices =
					stackalloc Entry<VertexTL>[face.EdgeCount];

				var k = 0;
				for (var j = face.FirstEdgeId; j < face.FirstEdgeId + face.EdgeCount; j++)
				{
					var id = File.SurfaceEdges.Data[j].Value;
					var vertexId = id > 0 ?
						File.Edges.Data[id].VertexID1 :
						File.Edges.Data[-id].VertexID2;

					var vertex = new VertexTL();
					var tex = File.TextureInfos.Data[face.TextureInfoId];
					vertex.Position = File.Vertexes.Data[vertexId].Point;
					// TODO: Load textures and calculate texture coords
					// TODO: Load lightmaps and calculate lightmap coords
					vertices[k].Index = vertexId;
					vertices[k].Value = vertex;
					k++;
				}
				callback(face, vertices);
			}
		}

		public static int GetFaceTriangleCount(ReadOnlySpan<Entry<VertexTL>> faceVerts) =>
			faceVerts.Length - 2;

		public static void Triangulate(ReadOnlySpan<Entry<VertexTL>> faceVerts,
			Span<Entry<VertexTL>> result)
		{
			var count = faceVerts.Length - 2;
			var root = faceVerts[0];
			var offset = 1;
			for (var i = 0; i < count * 3; i += 3)
			{
				result[i] = root;
				result[i + 1] = faceVerts[offset];
				result[i + 2] = faceVerts[offset + 1];
				offset++;
			}
		}
	}
}