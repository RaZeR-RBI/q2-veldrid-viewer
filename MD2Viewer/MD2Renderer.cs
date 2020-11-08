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
		private readonly GraphicsDevice _gd;

		private readonly TexturePool _texPool;
		private readonly DeviceBuffer _vertices;
		private readonly uint _vertexCount;

		private readonly DeviceBuffer _worldBuffer;
		private readonly DeviceBuffer _worldITBuffer;
		private readonly DeviceBuffer _paramsBuffer;
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
			new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
			new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
			new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
		);

		private static readonly VertexLayoutDescription s_vatVertexLayout = new VertexLayoutDescription(
			new VertexElementDescription("Index", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Int1),
			new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
		);

		private readonly Texture _vatPositionTex;
		private readonly Texture _vatNormalTex;
		private readonly DisposableArray<VATDescription> _vatFrames;

		private struct ShaderParameters
		{
			public Vector4 HalfPixel;
			public Vector4 Time;
			public Vector4 Translate;
			public Vector4 Scale;
		}

		private ShaderParameters _shaderParams = new ShaderParameters();
		private readonly ResourceSet _vatSet;

		public MD2Renderer(
			GraphicsDevice gd,
			IFileSystem fileSystem,
			MD2File file,
			IMemoryAllocator memAlloc,
			IArrayAllocator arrAlloc,
			DeviceBuffer viewBuf,
			DeviceBuffer projBuf,
			Camera camera)
		{
			_gd = gd;
			var reader = new MD2Reader(file, memAlloc);

			var rf = _gd.ResourceFactory;
			_worldBuffer = rf.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_worldITBuffer = rf.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_paramsBuffer = rf.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

			_gd.UpdateBuffer(_worldBuffer, 0, Matrix4x4.Identity);
			Matrix4x4.Invert(Matrix4x4.Identity, out Matrix4x4 worldInverted);
			_gd.UpdateBuffer(_worldITBuffer, 0, Matrix4x4.Transpose(worldInverted));

			_vertexCount = (uint)file.Triangles.Length * 3;
			var frame = reader.GetFrames()[0];
#if false
			_vertices = rf.CreateBuffer(new BufferDescription(
				_vertexCount * VertexNT.SizeInBytes, BufferUsage.VertexBuffer
			));
			Reader.ProcessFrame(frame, (name, data) =>
			{
				_gd.UpdateBuffer(_vertices, data);
			});

#else
			_vertices = rf.CreateBuffer(new BufferDescription(
				_vertexCount * VATVertex.SizeInBytes, BufferUsage.VertexBuffer
			));
			var vertData = new DisposableArray<VATVertex>((int)_vertexCount, memAlloc);
			reader.ProcessFrame(frame, (name, data) =>
			{
				for (var i = 0; i < _vertexCount; i++)
					vertData[i] = new VATVertex()
					{
						Index = i,
						UV = data[i].UV
					};
			});
			_gd.UpdateBuffer(_vertices, vertData.AsSpan());
			vertData.Dispose();
#endif
			var shaderSet = new ShaderSetDescription(
				new[] {
					// s_ntVertexLayout
					s_vatVertexLayout
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
					new ResourceLayoutElementDescription("WorldITBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
					new ResourceLayoutElementDescription("ParamsBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
				));
			var diffuseLayout = rf.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("DiffuseTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("DiffuseSampler", ResourceKind.Sampler, ShaderStages.Fragment)
				));
			var vatLayout = rf.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("VATPositionTexture", ResourceKind.TextureReadOnly, ShaderStages.Vertex),
					new ResourceLayoutElementDescription("VATNormalTexture", ResourceKind.TextureReadOnly, ShaderStages.Vertex),
					new ResourceLayoutElementDescription("VATSampler", ResourceKind.Sampler, ShaderStages.Vertex)
				));

			_pipeline = rf.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.SingleDisabled,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				RasterizerStateDescription.Default,
				PrimitiveTopology.TriangleList,
				shaderSet,
				new[] { projViewLayout, worldLayout, diffuseLayout, vatLayout },
				_gd.MainSwapchain.Framebuffer.OutputDescription
			));

			_projViewSet = rf.CreateResourceSet(new ResourceSetDescription(
				projViewLayout,
				projBuf,
				viewBuf));

			_worldParamSet = rf.CreateResourceSet(new ResourceSetDescription(
				worldLayout,
				_worldBuffer,
				_worldITBuffer,
				_paramsBuffer
			));

			_texPool = new TexturePool(gd, fileSystem, memAlloc, arrAlloc);
			for (var i = 0; i < file.Skins.Length; i++)
			{
				var path = file.Skins.Data[i].GetPath();
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

			_vatFrames = VertexAnimationTexture.CreateVAT(
				_gd,
				reader,
				memAlloc,
				out _vatPositionTex,
				out _vatNormalTex,
				out Vector3 translate,
				out Vector3 scale
			);
			_shaderParams.Translate = new Vector4(translate, 0);
			_shaderParams.Scale = new Vector4(scale, 1);
			_shaderParams.HalfPixel = new Vector4(0.5f, 0.5f, 0f, 0f) / new Vector4(_vatPositionTex.Width, _vatPositionTex.Height, 1f, 1f);
			_shaderParams.Time = Vector4.Zero;

			_gd.UpdateBuffer(_paramsBuffer, 0, _shaderParams);
			_vatSet = rf.CreateResourceSet(new ResourceSetDescription(
				vatLayout,
				_vatPositionTex,
				_vatNormalTex,
				_gd.LinearSampler
			));

			reader.Dispose();
		}

		public void Update(float deltaSeconds)
		{
			var maxTime = (float)_vatFrames.Length * 0.1f;
			var step = 1f / maxTime;
			var time = _shaderParams.Time.X + step * deltaSeconds;
			if (time > 1.0f) time -= 1.0f;
			_shaderParams.Time = new Vector4(time, 0f, 0f, 0f);
			_gd.UpdateBuffer(_paramsBuffer, 0, _shaderParams);
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
			cl.SetGraphicsResourceSet(3, _vatSet);
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
layout(set = 1, binding = 2) uniform ParamsBuffer
{
    vec4 HalfPixel;
    vec4 Time;
    vec4 Translate;
    vec4 Scale;
};

layout(set = 3, binding = 0) uniform texture2D VATPositionTexture;
layout(set = 3, binding = 1) uniform texture2D VATNormalTexture;
layout(set = 3, binding = 2) uniform sampler VATSampler;


layout(location = 0) in int Index;
layout(location = 1) in vec2 TexCoords;

layout(location = 0) out vec2 fsin_texCoords;
layout(location = 1) out vec3 fsin_normal;
void main()
{
	vec2 hp = HalfPixel.xy;
	vec2 vatCoords = vec2(Index * (hp.x * 2.0) + hp.x, Time.x + hp.y);
	vec3 texPosition = textureLod(sampler2D(VATPositionTexture, VATSampler), vatCoords, 0).xyz;
	texPosition *= Scale.xyz;
	texPosition += Translate.xyz;

    vec4 worldPosition = World * vec4(texPosition, 1);
    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;
	vec3 texNormal = textureLod(sampler2D(VATNormalTexture, VATSampler), vatCoords, 0).xyz;
	texNormal *= 2.0;
	texNormal -= vec3(1.0);
    gl_Position = clipPosition;
	fsin_texCoords = TexCoords;
    fsin_normal = normalize(mat3(WorldInverseTranspose) * texNormal);
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

	// half lambert with clamping
	float light = dot(fsin_normal, lightDir) * 0.5 + 0.5;
	light = clamp(light * light, 0.3, 1.0);

	fsout_color = vec4(rgb * light, 1.0);
}";
	}
}