using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Q2Viewer
{
	public class LightmapRenderer
	{
		private static readonly VertexLayoutDescription s_lmVertexLayout = new VertexLayoutDescription(
			new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
			new VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
			new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
			new VertexElementDescription("LMCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
		);

		private readonly GraphicsDevice _device;

		private readonly DeviceBuffer _worldBuffer;
		private readonly ResourceSet _projViewSet;
		private readonly ResourceSet _worldBufferSet;
		private readonly ResourceLayout _diffuseLayout;
		private readonly ResourceLayout _lightmapLayout;

		private readonly Pipeline _noBlendPipeline;

		private readonly Dictionary<Texture, ResourceSet> _textureSets = new Dictionary<Texture, ResourceSet>();

		public Camera Camera { get; set; }

		public LightmapRenderer(
			GraphicsDevice graphics,
			DeviceBuffer viewBuf,
			DeviceBuffer projBuf,
			Camera camera)
		{
			_device = graphics;
			Camera = camera;
			var factory = _device.ResourceFactory;

			_worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_device.UpdateBuffer(_worldBuffer, 0, Matrix4x4.Identity);

			var shaderSet = new ShaderSetDescription(
				new[] {
					s_lmVertexLayout
				},
				factory.CreateFromSpirv(
					new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexCode), "main"),
					new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentCode), "main")
				));

			var projViewLayout = factory.CreateResourceLayout(
					new ResourceLayoutDescription(
						new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
						new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
					));
			var worldLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
				));
			_diffuseLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("DiffuseTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("DiffuseSampler", ResourceKind.Sampler, ShaderStages.Fragment)
				));
			_lightmapLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("LightmapTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("LightmapSampler", ResourceKind.Sampler, ShaderStages.Fragment)
				));

			_noBlendPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.Empty,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				RasterizerStateDescription.Default,
				PrimitiveTopology.TriangleList,
				shaderSet,
				new[] { projViewLayout, worldLayout, _diffuseLayout, _lightmapLayout },
				_device.MainSwapchain.Framebuffer.OutputDescription
			));

			_projViewSet = factory.CreateResourceSet(new ResourceSetDescription(
				projViewLayout,
				projBuf,
				viewBuf));

			_worldBufferSet = factory.CreateResourceSet(new ResourceSetDescription(
				worldLayout,
				_worldBuffer
			));
		}

		private void CreateTextureSet(Texture texture, Sampler sampler, ResourceLayout layout)
		{
			var set = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				layout,
				texture,
				sampler
			));
			_textureSets.Add(texture, set);
		}

		public int Draw(CommandList cl, ModelRenderInfo mri, Matrix4x4 worldMatrix)
		{
			cl.UpdateBuffer(_worldBuffer, 0, worldMatrix);
			cl.SetPipeline(_noBlendPipeline);
			var calls = 0;
			var clipMatrix = Camera.ViewMatrix * Camera.ProjectionMatrix;
			// TODO: Render alpha-blended faces separately
			for (var i = 0; i < mri.FaceGroupsCount; i++)
			{
				ref var fg = ref mri.FaceGroups[i];
				if (Util.CheckIfOutside(clipMatrix, fg.Bounds))
					continue;

				var diffuseTex = fg.Texture;
				if (diffuseTex == null) continue;
				if (!_textureSets.ContainsKey(diffuseTex))
					CreateTextureSet(diffuseTex, _device.PointSampler, _diffuseLayout);
				var diffuseSet = _textureSets[diffuseTex];

				cl.SetVertexBuffer(0, fg.Buffer);
				cl.SetGraphicsResourceSet(0, _projViewSet);
				cl.SetGraphicsResourceSet(1, _worldBufferSet);
				cl.SetGraphicsResourceSet(2, diffuseSet);
				cl.SetGraphicsResourceSet(3, diffuseSet);
				// TODO: Bind lightmap
				cl.Draw(fg.Count);
				calls++;
			}
			return calls;
		}

		private const string VertexCode = @"
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
layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 TexCoords;
layout(location = 3) in vec2 LMCoords;

layout(location = 0) out vec2 fsin_texCoords;
layout(location = 1) out vec2 fsin_lmCoords;
void main()
{
    vec4 worldPosition = World * vec4(Position, 1);
    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;
    gl_Position = clipPosition;
    fsin_texCoords = TexCoords;
}";

		private const string FragmentCode = @"
#version 450
layout(location = 0) in vec2 fsin_texCoords;
layout(location = 1) in vec2 fsin_lmCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 2, binding = 0) uniform texture2D DiffuseTexture;
layout(set = 2, binding = 1) uniform sampler DiffuseSampler;
layout(set = 3, binding = 0) uniform texture2D LightmapTexture;
layout(set = 3, binding = 1) uniform sampler LightmapSampler;

void main()
{
    fsout_color =  texture(sampler2D(DiffuseTexture, DiffuseSampler), fsin_texCoords);
}";
	}
}