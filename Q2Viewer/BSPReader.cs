using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Q2Viewer
{
	public delegate void FaceVisitorCallback(LFace data, Span<Entry<VertexNTL>> vertices);

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

		public const float LightmapSizeF = 16f;
		public const int LightmapSize = 16;

		public IEnumerable<LModel> GetModels() =>
			File.Submodels.Data;

		public void ProcessVertices(LModel model, FaceVisitorCallback callback) =>
			ProcessVertices(Enumerable.Range(model.FirstFace, model.NumFaces), callback);

		public void ProcessVertices(IEnumerable<int> indexes, FaceVisitorCallback callback)
		{
			foreach (var i in indexes)
			{
				var face = File.Faces.Data[i];
				Span<Entry<VertexNTL>> vertices =
					stackalloc Entry<VertexNTL>[face.EdgeCount];

				var k = 0;
				for (var j = face.FirstEdgeId; j < face.FirstEdgeId + face.EdgeCount; j++)
				{
					var id = File.SurfaceEdges.Data[j].Value;
					ref var edge = ref File.Edges.Data[Math.Abs(id)];
					ref var plane = ref File.Planes.Data[face.PlaneId];

					var normal = plane.Normal;
					if (face.Side > 0) normal = -normal;

					var vertexId = id > 0 ? edge.VertexID1 : edge.VertexID2;

					var vertex = new VertexNTL();
					ref var tex = ref File.TextureInfos.Data[face.TextureInfoId];
					vertex.Position = File.Vertexes.Data[vertexId].Point;
					vertex.Normal = normal;
					var s = new Vector3(tex.S.X, tex.S.Y, tex.S.Z);
					var t = new Vector3(tex.T.X, tex.T.Y, tex.T.Z);
					// texture coordinates - need to divide by texture width and height
					vertex.UV.X = Vector3.Dot(vertex.Position, s) + tex.S.W;
					vertex.UV.Y = Vector3.Dot(vertex.Position, t) + tex.T.W;
					// lightmap coordinates - need to be adjusted to lightmap atlas
					vertex.LightmapUV = vertex.UV / LightmapSizeF;

					vertices[k].Index = vertexId;
					vertices[k].Value = vertex;
					k++;
				}
				callback(face, vertices);
			}
		}

		public static int GetFaceVertexCount(LFace face) =>
			3 + (face.EdgeCount - 3) * 3;

		public static int GetFaceTriangleCount(ReadOnlySpan<Entry<VertexNTL>> faceVerts) =>
			faceVerts.Length - 2;

		public static void Triangulate(ReadOnlySpan<Entry<VertexNTL>> faceVerts,
			Span<Entry<VertexNTL>> result)
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