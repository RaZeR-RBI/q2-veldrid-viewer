using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Common;

namespace Q2Viewer
{
	public delegate void FaceVisitorCallback(int faceIndex, LFace data, Span<Entry<VertexNTL>> vertices, Vector2i textureMins, Vector2i extents);

	public delegate void BrushVisitorCallback(LBrush brush, Span<Plane> planes);

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

		public Span<LModel> GetModels() => File.Submodels.Data;
		public Span<LBrush> GetBrushes() => File.Brushes.Data;

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
				var uvMin = new Vector2(float.MaxValue, float.MaxValue);
				var uvMax = new Vector2(float.MinValue, float.MinValue);
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
					// face extents
					uvMin = Vector2.Min(uvMin, vertex.UV);
					uvMax = Vector2.Max(uvMax, vertex.UV);
					// lightmap coordinates - need to be adjusted to lightmap atlas
					vertex.LightmapUV = vertex.UV;

					vertices[k].Index = vertexId;
					vertices[k].Value = vertex;
					k++;
				}
				var bmin = new Vector2i((int)MathF.Floor(uvMin.X / 16), (int)MathF.Floor(uvMin.Y / 16));
				var bmax = new Vector2i((int)MathF.Ceiling(uvMax.X / 16), (int)MathF.Ceiling(uvMax.Y / 16));
				var textureMins = bmin * 16;
				var extents = (bmax - bmin) * 16;

				// Debug.Assert(extents.X > 0);
				// Debug.Assert(extents.Y > 0);

				for (var l = 0; l < vertices.Length; l++)
				{
					ref var v = ref vertices[l].Value;
					v.LightmapUV -= (Vector2)textureMins;
					v.LightmapUV /= (Vector2)extents;
				}
				callback(i, face, vertices, textureMins, extents);
			}
		}

		public bool FindContainingLeaf(int faceIndex, out LLeaf leaf)
		{
			Debug.Assert(faceIndex >= 0);
			Debug.Assert(faceIndex < File.Faces.Length);
			for (var i = 0; i < File.Leaves.Length; i++)
			{
				var cur = File.Leaves.Data[i];
				for (var j = cur.FirstLeafFace; j < cur.FirstLeafFace + cur.NumLeafFaces; j++)
					if (File.LeafFaces.Data[j].Value == faceIndex)
					{
						leaf = cur;
						return true;
					}
			}
			leaf = new LLeaf();
			return false;
		}

		public void ProcessBrush(int brushIndex, BrushVisitorCallback cb) =>
			ProcessBrush(File.Brushes.Data[brushIndex], cb);

		public void ProcessBrush(LBrush brush, BrushVisitorCallback cb)
		{
			var sides = brush.NumSides;
			Span<Plane> planes = stackalloc Plane[sides];

			for (var i = 0; i < sides; i++)
			{
				var bsIndex = brush.FirstSide + i;
				var bs = File.BrushSides.Data[bsIndex];
				var plane = File.Planes.Data[bs.PlaneId].GetPlane();
				planes[i] = plane;
			}

			cb(brush, planes);
		}

		public static int GetFaceVertexCount(LFace face) =>
			3 + (face.EdgeCount - 3) * 3;
	}
}