using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Linq;
using Veldrid;
using Veldrid.SPIRV;

namespace Q2Viewer
{
	public class ModelRenderer
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
		private readonly ResourceSet _worldParamSet;
		private readonly DeviceBuffer _lightStyleBuffer;
		private readonly DeviceBuffer _alphaValueBuffer;
		private readonly ResourceSet _worldAlphaSet;
		private readonly ResourceLayout _diffuseLayout;
		private readonly ResourceLayout _lightmapLayout;

		private readonly Pipeline _noBlendPipeline;
		private readonly Pipeline _alphaBlendPipeline;
		private readonly Sampler _diffuseSampler;
		private readonly Sampler _lightmapSampler;

		private readonly Texture _whiteTexture;
		private readonly Dictionary<Texture, ResourceSet> _textureSets = new Dictionary<Texture, ResourceSet>();

		private static float[] s_transparency33 = new[] { 1f, 1f, 1f, 0.33f };
		private static float[] s_transparency66 = new[] { 1f, 1f, 1f, 0.66f };
		private const float s_lightStyleScale = (float)'m' - (float)'a';
		private const int c_maxLightStyles = 256;
		private static float[][] s_lightStyles = new[] {
			"m",
			"abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
			"mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
			"mamamamamama",
			"jklmnopqrstuvwxyzyxwvutsrqponmlkj",
			"nmonqnmomnmomomno",
			"mmmaaaabcdefgmmmmaaaammmaamm",
			"mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
			"aaaaaaaazzzzzzzz",
			"mmamammmmammamamaaamammma",
			"abcdefghijklmnopqrrqponmlkjihgfedcba",
		}.Select(s => s.Select(c => ((float)c - (float)'a') / s_lightStyleScale).ToArray())
			.ToArray();

		private long _frame = -1;
		private float[] _lightStyleValues = new float[c_maxLightStyles];

		public Camera Camera { get; set; }

		public ModelRenderer(
			GraphicsDevice graphics,
			DeviceBuffer viewBuf,
			DeviceBuffer projBuf,
			Camera camera)
		{
			_device = graphics;
			Camera = camera;
			var factory = _device.ResourceFactory;

			_whiteTexture = TexturePool.CreateWhiteTexture(_device);

			_worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_device.UpdateBuffer(_worldBuffer, 0, Matrix4x4.Identity);

			_lightStyleBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_device.UpdateBuffer(_lightStyleBuffer, 0, new Vector4(1, 0, 0, 0));

			var lmShaderSet = new ShaderSetDescription(
				new[] {
					s_lmVertexLayout
				},
				factory.CreateFromSpirv(
					new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(LightmappedVert), "main"),
					new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(LightmappedFrag), "main")
				));

			var projViewLayout = factory.CreateResourceLayout(
					new ResourceLayoutDescription(
						new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
						new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
					));
			var worldLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
					new ResourceLayoutElementDescription("ParamsBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)
				));
			_diffuseLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("DiffuseTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("DiffuseSampler", ResourceKind.Sampler, ShaderStages.Fragment)
				));
			// TODO: Investigate if it's better to unite them into 2d texture array
			_lightmapLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("LightmapSampler", ResourceKind.Sampler, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("LightmapTexture1", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("LightmapTexture2", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("LightmapTexture3", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("LightmapTexture4", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
				));

			_noBlendPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.Empty,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				RasterizerStateDescription.Default,
				PrimitiveTopology.TriangleList,
				lmShaderSet,
				new[] { projViewLayout, worldLayout, _diffuseLayout, _lightmapLayout },
				_device.MainSwapchain.Framebuffer.OutputDescription
			));

			_projViewSet = factory.CreateResourceSet(new ResourceSetDescription(
				projViewLayout,
				projBuf,
				viewBuf));

			_worldParamSet = factory.CreateResourceSet(new ResourceSetDescription(
				worldLayout,
				_worldBuffer,
				_lightStyleBuffer
			));

			_diffuseSampler = factory.CreateSampler(new SamplerDescription(
				SamplerAddressMode.Wrap,
				SamplerAddressMode.Wrap,
				SamplerAddressMode.Wrap,
				SamplerFilter.MinLinear_MagPoint_MipLinear,
				null,
				0,
				0,
				uint.MaxValue,
				0,
				SamplerBorderColor.OpaqueBlack
			));

			_lightmapSampler = _device.LinearSampler;

			var worldAlphaLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
				new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
				new ResourceLayoutElementDescription("AlphaValue", ResourceKind.UniformBuffer, ShaderStages.Fragment)
			));
			_alphaValueBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
			_worldAlphaSet = factory.CreateResourceSet(new ResourceSetDescription(
				worldAlphaLayout,
				_worldBuffer,
				_alphaValueBuffer
			));
			var abShaderSet = new ShaderSetDescription(
				new[] {
					s_lmVertexLayout
				},
				factory.CreateFromSpirv(
					new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(AlphaBlendVert), "main"),
					new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(AlphaBlendFrag), "main")
				));
			_alphaBlendPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.SingleAlphaBlend,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				RasterizerStateDescription.Default,
				PrimitiveTopology.TriangleList,
				abShaderSet,
				new[] { projViewLayout, worldAlphaLayout, _diffuseLayout },
				_device.MainSwapchain.Framebuffer.OutputDescription
			));
			NextAnimationFrame();
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

		private void CreateLightmapSet(LightmapList lightmaps, Sampler sampler)
		{
			var set = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				_lightmapLayout,
				sampler,
				lightmaps.Lightmap1,
				lightmaps.Lightmap2,
				lightmaps.Lightmap3,
				lightmaps.Lightmap4
			));
			_textureSets.Add(lightmaps.Lightmap1, set);
		}

		public void NextAnimationFrame()
		{
			_frame++;
			var styleCount = Math.Min(s_lightStyles.GetLength(0), c_maxLightStyles);
			for (var i = 0; i < styleCount; i++)
			{
				var index = (int)(_frame % s_lightStyles[i].Length);
				_lightStyleValues[i] = s_lightStyles[i][index];
			}
		}

		private bool IsTransparent(TexturedFaceGroup fg) =>
			fg.Flags.HasFlag(SurfaceFlags.Transparent33) ||
			fg.Flags.HasFlag(SurfaceFlags.Transparent66);

		public int Draw(CommandList cl, ModelRenderInfo mri, Matrix4x4 worldMatrix)
		{
			cl.UpdateBuffer(_worldBuffer, 0, worldMatrix);
			var calls = 0;
			var clipMatrix = Camera.ViewMatrix * Camera.ProjectionMatrix;
			cl.SetPipeline(_noBlendPipeline);
			ReadOnlySpan<float> lightStyles = _lightStyleValues.AsSpan();

			for (var i = 0; i < mri.FaceGroupsCount; i++)
			{
				ref var fg = ref mri.FaceGroups[i];
				if (IsTransparent(fg)) continue;
				DrawLightmapped(lightStyles, cl, ref fg, ref clipMatrix, ref calls);
			}
			cl.SetPipeline(_alphaBlendPipeline);
			for (var i = 0; i < mri.FaceGroupsCount; i++)
			{
				ref var fg = ref mri.FaceGroups[i];
				if (!IsTransparent(fg)) continue;
				DrawAlphaBlended(cl, ref fg, ref clipMatrix, ref calls);
			}
			return calls;
		}

		private float[] lightValues = new float[4];
		private void DrawLightmapped(ReadOnlySpan<float> lightStyles, CommandList cl, ref TexturedFaceGroup fg, ref Matrix4x4 clipMatrix, ref int calls)
		{
			if (Util.CheckIfOutside(clipMatrix, fg.Bounds))
				return;

			var diffuseTex = fg.Texture;
			if (diffuseTex == null) return;
			if (!_textureSets.ContainsKey(diffuseTex))
				CreateTextureSet(diffuseTex, _diffuseSampler, _diffuseLayout);
			var diffuseSet = _textureSets[diffuseTex];

			var lightmapTex = fg.Lightmap.IsNull() ? new LightmapList(() => _whiteTexture) : fg.Lightmap;
			if (!_textureSets.ContainsKey(lightmapTex.Lightmap1))
				CreateLightmapSet(lightmapTex, _lightmapSampler);
			var lightmapSet = _textureSets[lightmapTex.Lightmap1];

			lightValues[0] = fg.LightmapStyle1 == 255 ? 0 : lightStyles[fg.LightmapStyle1];
			lightValues[1] = fg.LightmapStyle2 == 255 ? 0 : lightStyles[fg.LightmapStyle2];
			lightValues[2] = fg.LightmapStyle3 == 255 ? 0 : lightStyles[fg.LightmapStyle3];
			lightValues[3] = fg.LightmapStyle4 == 255 ? 0 : lightStyles[fg.LightmapStyle4];
			cl.UpdateBuffer(_lightStyleBuffer, 0, lightValues);

			cl.SetVertexBuffer(0, fg.Buffer);
			cl.SetGraphicsResourceSet(0, _projViewSet);
			cl.SetGraphicsResourceSet(1, _worldParamSet);
			cl.SetGraphicsResourceSet(2, diffuseSet);
			cl.SetGraphicsResourceSet(3, lightmapSet);
			cl.Draw(fg.Count);
			calls++;
		}

		private void DrawAlphaBlended(CommandList cl, ref TexturedFaceGroup fg, ref Matrix4x4 clipMatrix, ref int calls)
		{
			var transparency = fg.Flags.HasFlag(SurfaceFlags.Transparent33) ? s_transparency33 : s_transparency66;
			if (Util.CheckIfOutside(clipMatrix, fg.Bounds))
				return;

			var diffuseTex = fg.Texture;
			if (diffuseTex == null) return;
			if (!_textureSets.ContainsKey(diffuseTex))
				CreateTextureSet(diffuseTex, _diffuseSampler, _diffuseLayout);
			var diffuseSet = _textureSets[diffuseTex];

			cl.UpdateBuffer(_alphaValueBuffer, 0, transparency);
			cl.SetVertexBuffer(0, fg.Buffer);
			cl.SetGraphicsResourceSet(0, _projViewSet);
			cl.SetGraphicsResourceSet(1, _worldAlphaSet);
			cl.SetGraphicsResourceSet(2, diffuseSet);
			cl.Draw(fg.Count);
			calls++;
		}


		// TODO: Pass gamma as uniform
		/* Lightmap shaders */
		private const string LightmappedVert = @"
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
	fsin_lmCoords = LMCoords;
}";

		private const string LightmappedFrag = @"
#version 450
layout(location = 0) in vec2 fsin_texCoords;
layout(location = 1) in vec2 fsin_lmCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform ParamsBuffer
{
	vec4 LightValues;
};

layout(set = 2, binding = 0) uniform texture2D DiffuseTexture;
layout(set = 2, binding = 1) uniform sampler DiffuseSampler;

layout(set = 3, binding = 0) uniform sampler LightmapSampler;
layout(set = 3, binding = 1) uniform texture2D LightmapTexture1;
layout(set = 3, binding = 2) uniform texture2D LightmapTexture2;
layout(set = 3, binding = 3) uniform texture2D LightmapTexture3;
layout(set = 3, binding = 4) uniform texture2D LightmapTexture4;

void main()
{
	vec3 color = texture(sampler2D(DiffuseTexture, DiffuseSampler), fsin_texCoords).xyz;
	vec3 lm1 = texture(sampler2D(LightmapTexture1, LightmapSampler), fsin_lmCoords).rgb * LightValues.x;
	vec3 lm2 = texture(sampler2D(LightmapTexture2, LightmapSampler), fsin_lmCoords).rgb * LightValues.y;
	vec3 lm3 = texture(sampler2D(LightmapTexture3, LightmapSampler), fsin_lmCoords).rgb * LightValues.z;
	vec3 lm4 = texture(sampler2D(LightmapTexture4, LightmapSampler), fsin_lmCoords).rgb * LightValues.w;
	vec3 lm = lm1 + lm2 + lm3 + lm4;

	vec3 gamma = vec3(1.0 / 2.2);
	fsout_color = vec4(pow(color * lm, gamma), 1);
}";

		private const string AlphaBlendVert = @"
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

layout(location = 0) out vec2 fsin_texCoords;
void main()
{
    vec4 worldPosition = World * vec4(Position, 1);
    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;
    gl_Position = clipPosition;
    fsin_texCoords = TexCoords;
}";

		private const string AlphaBlendFrag = @"
#version 450
layout(location = 0) in vec2 fsin_texCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform AlphaValue
{
	vec4 Alpha;
};
layout(set = 2, binding = 0) uniform texture2D DiffuseTexture;
layout(set = 2, binding = 1) uniform sampler DiffuseSampler;

void main()
{
	vec4 color = texture(sampler2D(DiffuseTexture, DiffuseSampler), fsin_texCoords);
	vec4 gamma = vec4(1.0 / 2.2);
	vec3 rgb = pow(color.xyz, gamma.xyz);
	fsout_color = vec4(rgb, color.w * Alpha.w);
}";
	}
}