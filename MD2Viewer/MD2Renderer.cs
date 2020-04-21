using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Common;
using SharpFileSystem;
using Veldrid;
using Veldrid.SPIRV;
using static Imagini.Logger;

namespace MD2Viewer
{
	public class MD2Renderer
	{
		public readonly MD2File File;
		public readonly MD2Reader Reader;
		private readonly IArrayAllocator _allocator;
		private readonly GraphicsDevice _gd;

		private readonly TexturePool _texPool;
		private readonly DeviceBuffer _vertices;
		private readonly uint _vertexCount;

		private readonly DeviceBuffer _worldBuffer;
		private readonly DeviceBuffer _worldITBuffer;
		private readonly ResourceSet _projViewSet;
		private readonly ResourceSet _worldParamSet;
		private readonly Pipeline _pipeline;
		private readonly List<ResourceSet> _diffuseTextures = new List<ResourceSet>();

		private int _selectedSkin = 0;
		public int SkinCount => _diffuseTextures.Count;
		public int SelectedSkin
		{
			get => _selectedSkin;
			set
			{
				_selectedSkin = value % SkinCount;
			}
		}

		private static readonly VertexLayoutDescription s_ntVertexLayout = new VertexLayoutDescription(
			new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
			new VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
			new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
		);

		public MD2Renderer(
			GraphicsDevice gd,
			IFileSystem fileSystem,
			MD2File file,
			IArrayAllocator allocator,
			DeviceBuffer viewBuf,
			DeviceBuffer projBuf,
			Camera camera)
		{
			File = file;
			_allocator = allocator;
			_gd = gd;
			Reader = new MD2Reader(File, _allocator);

			var rf = _gd.ResourceFactory;
			_worldBuffer = rf.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_worldITBuffer = rf.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

			_gd.UpdateBuffer(_worldBuffer, 0, Matrix4x4.Identity);
			Matrix4x4.Invert(Matrix4x4.Identity, out Matrix4x4 worldInverted);
			_gd.UpdateBuffer(_worldITBuffer, 0, Matrix4x4.Transpose(worldInverted));

			// TODO: Pack frame data into texture and use it for animation
			_vertexCount = (uint)File.Triangles.Length * 3;
			_vertices = rf.CreateBuffer(new BufferDescription(
				_vertexCount * VertexNT.SizeInBytes, BufferUsage.VertexBuffer
			));
			var frame = Reader.GetFrames().First();
			Reader.ProcessFrame(frame, (name, data) =>
			{
				_gd.UpdateBuffer(_vertices, data);
			});

			var shaderSet = new ShaderSetDescription(
				new[] {
					s_ntVertexLayout
				},
				rf.CreateFromSpirv(
					new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexShader), "main"),
					new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentShader), "main")
				));

			var projViewLayout = rf.CreateResourceLayout(
					new ResourceLayoutDescription(
						new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
						new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
					));
			var worldLayout = rf.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
					new ResourceLayoutElementDescription("WorldITBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
				));
			var diffuseLayout = rf.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("DiffuseTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("DiffuseSampler", ResourceKind.Sampler, ShaderStages.Fragment)
				));

			_pipeline = rf.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.Empty,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				RasterizerStateDescription.Default,
				PrimitiveTopology.TriangleList,
				shaderSet,
				new[] { projViewLayout, worldLayout, diffuseLayout },
				_gd.MainSwapchain.Framebuffer.OutputDescription
			));

			_projViewSet = rf.CreateResourceSet(new ResourceSetDescription(
				projViewLayout,
				projBuf,
				viewBuf));

			_worldParamSet = rf.CreateResourceSet(new ResourceSetDescription(
				worldLayout,
				_worldBuffer,
				_worldITBuffer
			));

			_texPool = new TexturePool(gd, fileSystem, allocator);
			for (var i = 0; i < file.Skins.Length; i++)
			{
				var path = file.Skins.Data[i].Path;
				var skinTex = _texPool.LoadAbsolute(path);
				if (skinTex == null)
				{
					Log.Warning($"Unable to load texture '{path}'");
					continue;
				}
				_diffuseTextures.Add(CreateTextureSet(skinTex, diffuseLayout));
			}
			if (_diffuseTextures.Count == 0)
				_diffuseTextures.Add(CreateTextureSet(_texPool.GetTexture(null), diffuseLayout));
		}

		public void Draw(CommandList cl, Matrix4x4 worldMatrix)
		{
			cl.UpdateBuffer(_worldBuffer, 0, worldMatrix);
			Matrix4x4.Invert(worldMatrix, out Matrix4x4 inverted);
			cl.UpdateBuffer(_worldITBuffer, 0, Matrix4x4.Transpose(inverted));
			cl.SetVertexBuffer(0, _vertices);
			cl.SetPipeline(_pipeline);

			cl.SetGraphicsResourceSet(0, _projViewSet);
			cl.SetGraphicsResourceSet(1, _worldParamSet);
			cl.SetGraphicsResourceSet(2, _diffuseTextures[SelectedSkin]);
			cl.Draw(_vertexCount);
		}

		private ResourceSet CreateTextureSet(Texture texture, ResourceLayout layout) =>
			_gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				layout,
				texture,
				_gd.PointSampler
			));

		private const string VertexShader = @"
#version 450
layout(set = 0, binding = 0) uniform ProjectionBuffer
{
    mat4 Projection;
};
layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
};
layout(set = 1, binding = 0) uniform WorldBuffer
{
    mat4 World;
};
layout(set = 1, binding = 1) uniform WorldITBuffer
{
    mat4 WorldInverseTranspose;
};
layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 TexCoords;

layout(location = 0) out vec2 fsin_texCoords;
layout(location = 1) out vec3 fsin_normal;
void main()
{
    vec4 worldPosition = World * vec4(Position, 1);
    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;
    gl_Position = clipPosition;
	fsin_texCoords = TexCoords;
    fsin_normal = normalize(mat3(WorldInverseTranspose) * Normal);
}";

		private const string FragmentShader = @"
#version 450
layout(location = 0) in vec2 fsin_texCoords;
layout(location = 1) in vec3 fsin_normal;
layout(location = 0) out vec4 fsout_color;

layout(set = 2, binding = 0) uniform texture2D DiffuseTexture;
layout(set = 2, binding = 1) uniform sampler DiffuseSampler;

void main()
{
    vec3 lightDir = vec3(0, 0, 1);

	float intensity = 1.5;
	vec3 color = texture(sampler2D(DiffuseTexture, DiffuseSampler), fsin_texCoords).xyz * intensity;
	vec3 gamma = vec3(1.0 / 2.2);
	vec3 rgb = pow(color, gamma);

    float light = clamp(dot(fsin_normal, lightDir), 0.3, 1.0);

	fsout_color = vec4(rgb * light, 1.0);
}";
	}
}