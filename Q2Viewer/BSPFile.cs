using System;

namespace Q2Viewer
{
	public class BSPFile : IDisposable
	{
		public readonly Lump<LVertexPosition> Vertexes;
		public readonly Lump<LEdge> Edges;
		public readonly Lump<LIntValue> SurfaceEdges;
		public readonly Lump<LRawValue> Lighting;
		public readonly Lump<LPlane> Planes;
		public readonly Lump<LTextureInfo> TextureInfos;
		public readonly Lump<LFace> Faces;
		public readonly Lump<LShortValue> Marksurfaces;
		public readonly Lump<LRawValue> Visibility;
		public readonly Lump<LLeaf> Leaves;
		public readonly Lump<LModel> Submodels;

		public void Dispose()
		{
			throw new NotImplementedException();
		}
	}
}