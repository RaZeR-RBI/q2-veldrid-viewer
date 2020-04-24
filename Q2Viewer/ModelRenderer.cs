using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Linq;
using Veldrid;
using Veldrid.SPIRV;
using static Common.Util;
using Common;

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

		private static readonly VertexLayoutDescription s_vertexPosLayout = new VertexLayoutDescription(
			new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
		);

		private struct CommonShaderParams
		{
			public float Gamma;
			public float Scroll;
			public float WarpAngle;
			public float Factor;
		}

		private readonly GraphicsDevice _device;

		private readonly DeviceBuffer _worldBuffer;
		private readonly ResourceSet _projViewSet;
		private readonly ResourceSet _worldParamSet;
		private readonly DeviceBuffer _commonParamsBuffer;
		private readonly DeviceBuffer _lmParamsBuffer;
		private readonly ResourceSet _worldAlphaSet;
		private readonly ResourceLayout _diffuseLayout;
		private readonly ResourceLayout _lightmapLayout;
		private readonly ResourceLayout _skyboxLayout;
		private readonly DeviceBuffer _skyboxVerts;
		private readonly DeviceBuffer _skyboxIndices;

		private readonly Pipeline _noBlendPipeline;
		private readonly Pipeline _alphaBlendPipeline;
		private readonly Pipeline _skyboxPipeline;
		private readonly Sampler _diffuseSampler;
		private readonly Sampler _lightmapSampler;

		private readonly Texture _lmFallbackTexture;
		private readonly Dictionary<Texture, ResourceSet> _textureSets = new Dictionary<Texture, ResourceSet>();

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

		private CommonShaderParams _commonParams = new CommonShaderParams()
		{
			Gamma = 2.2f,
		};

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

			_lmFallbackTexture = TexturePool.CreateSingleColorTexture(
				_device,
				layers: LightmapAllocator.LightmapsPerFace,
				new ColorRGBA(20, 20, 20));

			_worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_device.UpdateBuffer(_worldBuffer, 0, Matrix4x4.Identity);

			_commonParamsBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_device.UpdateBuffer(_commonParamsBuffer, 0, _commonParams);

			_lmParamsBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_device.UpdateBuffer(_lmParamsBuffer, 0, new Vector4(1, 0, 0, 0));

			var lmShaderSet = new ShaderSetDescription(
				new[] {
					s_lmVertexLayout
				},
				factory.CreateFromSpirv(
					new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexShader), "main"),
					new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(LightmappedFrag), "main")
				));

			var projViewLayout = factory.CreateResourceLayout(
					new ResourceLayoutDescription(
						new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
						new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
					));
			var lmParamsLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
					new ResourceLayoutElementDescription("ParamsBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
					new ResourceLayoutElementDescription("LightStylesBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)
				));
			_diffuseLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("DiffuseTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("DiffuseSampler", ResourceKind.Sampler, ShaderStages.Fragment)
				));
			_lightmapLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("LightmapSampler", ResourceKind.Sampler, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("LightmapTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
				));

			_noBlendPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.Empty,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				RasterizerStateDescription.Default,
				PrimitiveTopology.TriangleList,
				lmShaderSet,
				new[] { projViewLayout, lmParamsLayout, _diffuseLayout, _lightmapLayout },
				_device.MainSwapchain.Framebuffer.OutputDescription
			));

			_projViewSet = factory.CreateResourceSet(new ResourceSetDescription(
				projViewLayout,
				projBuf,
				viewBuf));

			_worldParamSet = factory.CreateResourceSet(new ResourceSetDescription(
				lmParamsLayout,
				_worldBuffer,
				_commonParamsBuffer,
				_lmParamsBuffer
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
				new ResourceLayoutElementDescription("ParamsBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment | ShaderStages.Vertex)
			));
			_worldAlphaSet = factory.CreateResourceSet(new ResourceSetDescription(
				worldAlphaLayout,
				_worldBuffer,
				_commonParamsBuffer
			));
			var abShaderSet = new ShaderSetDescription(
				new[] {
					s_lmVertexLayout
				},
				factory.CreateFromSpirv(
					new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexShader), "main"),
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

			_skyboxLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("SkyboxTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
					new ResourceLayoutElementDescription("SkyboxSampler", ResourceKind.Sampler, ShaderStages.Fragment)
				));
			var sbShaderSet = new ShaderSetDescription(new[] {
				s_vertexPosLayout
			},
			factory.CreateFromSpirv(
				new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(SkyboxVert), "main"),
				new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(SkyboxFrag), "main")
			));

			_skyboxPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.SingleAlphaBlend,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				// RasterizerStateDescription.Default,
				new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, true),
				PrimitiveTopology.TriangleList,
				sbShaderSet,
				new[] { projViewLayout, _skyboxLayout },
				_device.MainSwapchain.Framebuffer.OutputDescription
			));

			_skyboxVerts = factory.CreateBuffer(new BufferDescription((uint)s_skyboxVerts.Length * VertexPosition.SizeInBytes, BufferUsage.VertexBuffer));
			_device.UpdateBuffer(_skyboxVerts, 0, s_skyboxVerts);
			_skyboxIndices = factory.CreateBuffer(new BufferDescription((uint)sizeof(ushort) * (uint)s_skyboxIndices.Length, BufferUsage.IndexBuffer));
			_device.UpdateBuffer(_skyboxIndices, 0, s_skyboxIndices);
			NextLightmapStyleFrame();
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

		private void CreateLightmapSet(Texture lightmap, Sampler sampler)
		{
			var set = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
				_lightmapLayout,
				sampler,
				lightmap
			));
			_textureSets.Add(lightmap, set);
		}

		private float _lmAnimTime = 0f;
		private const float c_lmFrameTime = 0.01f;
		private float _texScroll = 0f;
		private const float c_texScrollSpeed = 40f / 64f;
		private float _texWarp = 0f;
		private const float c_maxTexWarp = MathF.PI * 2;

		public void Update(float deltaSeconds)
		{
			_lmAnimTime += deltaSeconds;
			if (_lmAnimTime >= c_lmFrameTime)
			{
				_lmAnimTime -= 0.01f;
				NextLightmapStyleFrame();
			}
			_texScroll += deltaSeconds * c_texScrollSpeed;
			_texWarp += deltaSeconds;
			if (_texScroll >= 1f) _texScroll -= 1f;
			if (_texWarp >= c_maxTexWarp) _texWarp -= c_maxTexWarp;
		}

		private void NextLightmapStyleFrame()
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
			cl.SetVertexBuffer(0, mri.Buffer);
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
			if (CheckIfOutside(clipMatrix, fg.Bounds))
				return;

			var diffuseTex = fg.Texture;
			if (diffuseTex == null) return;
			if (!_textureSets.ContainsKey(diffuseTex))
				CreateTextureSet(diffuseTex, _diffuseSampler, _diffuseLayout);
			var diffuseSet = _textureSets[diffuseTex];

			var lightmapTex = fg.Lightmap ?? _lmFallbackTexture;
			if (!_textureSets.ContainsKey(lightmapTex))
				CreateLightmapSet(lightmapTex, _lightmapSampler);
			var lightmapSet = _textureSets[lightmapTex];

			var common = _commonParams;
			common.Scroll = fg.Flags.HasFlag(SurfaceFlags.Flowing) ? -_texScroll : 0f;
			common.WarpAngle = fg.Flags.HasFlag(SurfaceFlags.Warp) ? _texWarp : 0f;
			cl.UpdateBuffer(_commonParamsBuffer, 0, common);

			lightValues[0] = fg.LightmapStyle1 == 255 ? 0 : lightStyles[fg.LightmapStyle1];
			lightValues[1] = fg.LightmapStyle2 == 255 ? 0 : lightStyles[fg.LightmapStyle2];
			lightValues[2] = fg.LightmapStyle3 == 255 ? 0 : lightStyles[fg.LightmapStyle3];
			lightValues[3] = fg.LightmapStyle4 == 255 ? 0 : lightStyles[fg.LightmapStyle4];
			cl.UpdateBuffer(_lmParamsBuffer, 0, lightValues);

			cl.SetGraphicsResourceSet(0, _projViewSet);
			cl.SetGraphicsResourceSet(1, _worldParamSet);
			cl.SetGraphicsResourceSet(2, diffuseSet);
			cl.SetGraphicsResourceSet(3, lightmapSet);
			cl.Draw(fg.Count, 1, fg.BufferOffset, 0);
			calls++;
		}

		private void DrawAlphaBlended(CommandList cl, ref TexturedFaceGroup fg, ref Matrix4x4 clipMatrix, ref int calls)
		{
			if (CheckIfOutside(clipMatrix, fg.Bounds))
				return;

			var diffuseTex = fg.Texture;
			if (diffuseTex == null) return;
			if (!_textureSets.ContainsKey(diffuseTex))
				CreateTextureSet(diffuseTex, _diffuseSampler, _diffuseLayout);
			var diffuseSet = _textureSets[diffuseTex];

			var common = _commonParams;
			common.Scroll = fg.Flags.HasFlag(SurfaceFlags.Flowing) ? -_texScroll : 0f;
			common.WarpAngle = fg.Flags.HasFlag(SurfaceFlags.Warp) ? _texWarp : 0f;
			common.Factor = fg.Flags.HasFlag(SurfaceFlags.Transparent33) ? 0.33f : 0.66f;
			cl.UpdateBuffer(_commonParamsBuffer, 0, common);

			cl.SetGraphicsResourceSet(0, _projViewSet);
			cl.SetGraphicsResourceSet(1, _worldAlphaSet);
			cl.SetGraphicsResourceSet(2, diffuseSet);
			cl.Draw(fg.Count, 1, fg.BufferOffset, 0);
			calls++;
		}

		public void DrawSkybox(CommandList cl, Texture texture)
		{
			cl.SetPipeline(_skyboxPipeline);
			cl.SetVertexBuffer(0, _skyboxVerts);
			cl.SetIndexBuffer(_skyboxIndices, IndexFormat.UInt16);
			cl.SetGraphicsResourceSet(0, _projViewSet);
			if (!_textureSets.ContainsKey(texture))
				CreateTextureSet(texture, _device.LinearSampler, _skyboxLayout);
			var textureSet = _textureSets[texture];
			cl.SetGraphicsResourceSet(1, textureSet);
			var fb = _device.MainSwapchain.Framebuffer;
			cl.SetViewport(0, new Viewport(0, 0, fb.Width, fb.Height, 1, 1));
			cl.DrawIndexed((uint)s_skyboxIndices.Length, 1, 0, 0, 0);
			cl.SetViewport(0, new Viewport(0, 0, fb.Width, fb.Height, 0, 1));
		}


		// TODO: Optimize - replace separate World, View and Projection matrices with one matrix
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
layout(set = 1, binding = 1) uniform ParamsBuffer
{
	float Gamma;
	float Scroll;
	float WarpAngle;
	float Factor;
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
    vec2 tx = TexCoords + vec2(Scroll, 0.0);
	if (WarpAngle != 0.0) {
		tx.s += sin(tx.t * 0.125 + WarpAngle) * 0.0625;
		tx.t -= sin(tx.s * 0.125 + WarpAngle) * 0.0625;
	}
	fsin_texCoords = tx;
	fsin_lmCoords = LMCoords;
}";

		private const string LightmappedFrag = @"
#version 450
layout(location = 0) in vec2 fsin_texCoords;
layout(location = 1) in vec2 fsin_lmCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform ParamsBuffer
{
	float Gamma;
	float Scroll;
	float WarpAngle;
	float Factor;
};

layout(set = 1, binding = 2) uniform LightStylesBuffer
{
	vec4 LightValues;
};

layout(set = 2, binding = 0) uniform texture2D DiffuseTexture;
layout(set = 2, binding = 1) uniform sampler DiffuseSampler;

layout(set = 3, binding = 0) uniform sampler LightmapSampler;
layout(set = 3, binding = 1) uniform texture2DArray LightmapTexture;

void main()
{
	float intensity = 1.5;
	float overbright = 1.3;
	vec3 gamma = vec3(1.0 / Gamma);

	vec3 color = texture(sampler2D(DiffuseTexture, DiffuseSampler), fsin_texCoords).xyz * intensity;
	vec3 lm1 = texture(sampler2DArray(LightmapTexture, LightmapSampler), vec3(fsin_lmCoords, 0)).rgb * LightValues.x;
	vec3 lm2 = texture(sampler2DArray(LightmapTexture, LightmapSampler), vec3(fsin_lmCoords, 1)).rgb * LightValues.y;
	vec3 lm3 = texture(sampler2DArray(LightmapTexture, LightmapSampler), vec3(fsin_lmCoords, 2)).rgb * LightValues.z;
	vec3 lm4 = texture(sampler2DArray(LightmapTexture, LightmapSampler), vec3(fsin_lmCoords, 3)).rgb * LightValues.w;
	vec3 lm = lm1 + lm2 + lm3 + lm4;
	lm *= overbright;

	fsout_color = vec4(pow(color * lm, gamma), 1);
}";

		private const string AlphaBlendFrag = @"
#version 450
layout(location = 0) in vec2 fsin_texCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform ParamsBuffer
{
	float Gamma;
	float Scroll;
	float WarpAngle;
	float Factor;
};

layout(set = 2, binding = 0) uniform texture2D DiffuseTexture;
layout(set = 2, binding = 1) uniform sampler DiffuseSampler;

void main()
{
	float intensity = 1.5;
	vec3 color = texture(sampler2D(DiffuseTexture, DiffuseSampler), fsin_texCoords).xyz * intensity;
	vec3 gamma = vec3(1.0 / Gamma);
	vec3 rgb = pow(color, gamma);
	fsout_color = vec4(rgb, Factor);
}";

		private const string SkyboxVert = @"
#version 450
layout (location = 0) in vec3 Position;
layout (location = 0) out vec3 TexCoords;

layout(set = 0, binding = 0) uniform ProjectionBuffer
{
    mat4 Projection;
};
layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
};

void main()
{
	mat4 view3x3 = mat4(
        View[0][0], View[0][1], View[0][2], 0,
        View[1][0], View[1][1], View[1][2], 0,
        View[2][0], View[2][1], View[2][2], 0,
        0, 0, 0, 1);
    vec4 pos = Projection * view3x3 * vec4(Position, 1);
    gl_Position = vec4(pos.x, pos.y, pos.w, pos.w);
	TexCoords = Position;
}
		";

		private const string SkyboxFrag = @"
#version 450

layout(location = 0) in vec3 TexCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 0) uniform textureCube SkyboxTexture;
layout(set = 1, binding = 1) uniform sampler SkyboxSampler;

void main()
{
	vec3 color = texture(samplerCube(SkyboxTexture, SkyboxSampler), TexCoords).xyz;
	vec3 gamma = vec3(1.0 / 2.2); // TODO: Configurable gamma
	fsout_color = vec4(pow(color, gamma), 1);
}
		";
		private static readonly Vector3[] s_skyboxVerts = new Vector3[]
		{
            // Top
            new Vector3(-20.0f,20.0f,-20.0f),
			new Vector3(20.0f,20.0f,-20.0f),
			new Vector3(20.0f,20.0f,20.0f),
			new Vector3(-20.0f,20.0f,20.0f),
            // Bottom
            new Vector3(-20.0f,-20.0f,20.0f),
			new Vector3(20.0f,-20.0f,20.0f),
			new Vector3(20.0f,-20.0f,-20.0f),
			new Vector3(-20.0f,-20.0f,-20.0f),
            // Left
            new Vector3(-20.0f,20.0f,-20.0f),
			new Vector3(-20.0f,20.0f,20.0f),
			new Vector3(-20.0f,-20.0f,20.0f),
			new Vector3(-20.0f,-20.0f,-20.0f),
            // Right
            new Vector3(20.0f,20.0f,20.0f),
			new Vector3(20.0f,20.0f,-20.0f),
			new Vector3(20.0f,-20.0f,-20.0f),
			new Vector3(20.0f,-20.0f,20.0f),
            // Back
            new Vector3(20.0f,20.0f,-20.0f),
			new Vector3(-20.0f,20.0f,-20.0f),
			new Vector3(-20.0f,-20.0f,-20.0f),
			new Vector3(20.0f,-20.0f,-20.0f),
            // Front
            new Vector3(-20.0f,20.0f,20.0f),
			new Vector3(20.0f,20.0f,20.0f),
			new Vector3(20.0f,-20.0f,20.0f),
			new Vector3(-20.0f,-20.0f,20.0f),
		};

		private static readonly ushort[] s_skyboxIndices = new ushort[]
		{
			0,1,2, 0,2,3,
			4,5,6, 4,6,7,
			8,9,10, 8,10,11,
			12,13,14, 12,14,15,
			16,17,18, 16,18,19,
			20,21,22, 20,22,23,
		};
	}
}