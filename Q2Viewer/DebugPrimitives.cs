using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Q2Viewer
{
	public class DebugPrimitives
	{
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
layout(location = 1) in vec3 Color;
layout(location = 0) out vec3 fsin_Color;
void main()
{
    vec4 worldPosition = World * vec4(Position, 1);
    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;
    gl_Position = clipPosition;
    fsin_Color = Color;
}";

		private const string FragmentCode = @"
#version 450
layout(location = 0) in vec3 fsin_Color;
layout(location = 0) out vec4 fsout_Color;
void main()
{
    fsout_Color = vec4(fsin_Color, 1);
}";

		private readonly GraphicsDevice _device;

		private const float c_gizmoSize = 16.0f;

		private static VertexColor[] s_gizmoVertices = {
			new VertexColor(Vector3.Zero, RgbaFloat.Red),
			new VertexColor(Vector3.UnitX * c_gizmoSize, RgbaFloat.Red),
			new VertexColor(Vector3.Zero, RgbaFloat.Green),
			new VertexColor(Vector3.UnitY * c_gizmoSize, RgbaFloat.Green),
			new VertexColor(Vector3.Zero, RgbaFloat.Blue),
			new VertexColor(Vector3.UnitZ * c_gizmoSize, RgbaFloat.Blue),
		};

		private readonly DeviceBuffer _gizmoVertexBuffer;
		private readonly DeviceBuffer _cubeVertexBuffer;
		private readonly DeviceBuffer _cubeIndexBuffer;

		private static readonly VertexLayoutDescription s_colorVertexLayout = new VertexLayoutDescription(
			new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
			new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float3)
		);

		private readonly Pipeline _linePipeline;
		private readonly Pipeline _tlPipeline;
		private readonly DeviceBuffer _worldBuffer;
		private readonly ResourceSet _projViewSet;
		private readonly ResourceSet _worldBufferSet;
		public Camera Camera { get; set; }

		public DebugPrimitives(
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

			var projViewLayout = factory.CreateResourceLayout(
					new ResourceLayoutDescription(
						new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
						new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
					));
			var worldLayout = factory.CreateResourceLayout(
				new ResourceLayoutDescription(
					new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
				));
			var shaderSet = new ShaderSetDescription(
				new[] {
					s_colorVertexLayout
				},
				factory.CreateFromSpirv(
					new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexCode), "main"),
					new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentCode), "main")
				));

			_linePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.SingleOverrideBlend,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				RasterizerStateDescription.CullNone,
				PrimitiveTopology.LineList,
				shaderSet,
				new[] { projViewLayout, worldLayout },
				_device.MainSwapchain.Framebuffer.OutputDescription
			));

			_tlPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
				BlendStateDescription.SingleOverrideBlend,
				DepthStencilStateDescription.DepthOnlyLessEqual,
				RasterizerStateDescription.Default,
				PrimitiveTopology.TriangleList,
				shaderSet,
				new[] { projViewLayout, worldLayout },
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

			_gizmoVertexBuffer = factory.CreateBuffer(new BufferDescription(
				VertexColor.SizeInBytes * (uint)s_gizmoVertices.Length, BufferUsage.VertexBuffer
			));
			_device.UpdateBuffer(_gizmoVertexBuffer, 0, s_gizmoVertices);

			var cubeVerts = GetCubeVertices(RgbaFloat.Green);
			var cubeIndices = GetCubeIndices();
			_cubeVertexBuffer = factory.CreateBuffer(new BufferDescription(
				VertexColor.SizeInBytes * (uint)cubeVerts.Length, BufferUsage.VertexBuffer
			));
			_device.UpdateBuffer(_cubeVertexBuffer, 0, cubeVerts);
			_cubeIndexBuffer = factory.CreateBuffer(new BufferDescription(
				sizeof(ushort) * (uint)cubeIndices.Length, BufferUsage.IndexBuffer
			));
			_device.UpdateBuffer(_cubeIndexBuffer, 0, cubeIndices);
		}

		public void DrawLines(
			CommandList cl,
			Matrix4x4 worldMatrix,
			DeviceBuffer lineBuffer,
			uint count)
		{
			cl.UpdateBuffer(_worldBuffer, 0, worldMatrix);
			cl.SetPipeline(_linePipeline);
			cl.SetVertexBuffer(0, lineBuffer);
			cl.SetGraphicsResourceSet(0, _projViewSet);
			cl.SetGraphicsResourceSet(1, _worldBufferSet);
			cl.Draw(count);
		}

		public void DrawGizmo(CommandList cl) =>
			DrawLines(cl, Matrix4x4.Identity, _gizmoVertexBuffer, 6);

		public void DrawCube(CommandList cl, Vector3 position)
		{
			var world = Matrix4x4.CreateTranslation(position);
			cl.UpdateBuffer(_worldBuffer, 0, world);
			cl.SetPipeline(_tlPipeline);
			cl.SetVertexBuffer(0, _cubeVertexBuffer);
			cl.SetIndexBuffer(_cubeIndexBuffer, IndexFormat.UInt16);
			cl.SetGraphicsResourceSet(0, _projViewSet);
			cl.SetGraphicsResourceSet(1, _worldBufferSet);
			cl.DrawIndexed(36, 1, 0, 0, 0);
		}

		private static VertexColor[] GetCubeVertices(RgbaFloat color)
		{
			VertexColor[] vertices = new VertexColor[]
			{
                // Top
                new VertexColor(new Vector3(-0.5f, +0.5f, -0.5f), color),
				new VertexColor(new Vector3(+0.5f, +0.5f, -0.5f), color),
				new VertexColor(new Vector3(+0.5f, +0.5f, +0.5f), color),
				new VertexColor(new Vector3(-0.5f, +0.5f, +0.5f), color),
                // Bottom
                new VertexColor(new Vector3(-0.5f,-0.5f, +0.5f),  color),
				new VertexColor(new Vector3(+0.5f,-0.5f, +0.5f),  color),
				new VertexColor(new Vector3(+0.5f,-0.5f, -0.5f),  color),
				new VertexColor(new Vector3(-0.5f,-0.5f, -0.5f),  color),
                // Left
                new VertexColor(new Vector3(-0.5f, +0.5f, -0.5f), color),
				new VertexColor(new Vector3(-0.5f, +0.5f, +0.5f), color),
				new VertexColor(new Vector3(-0.5f, -0.5f, +0.5f), color),
				new VertexColor(new Vector3(-0.5f, -0.5f, -0.5f), color),
                // Right
                new VertexColor(new Vector3(+0.5f, +0.5f, +0.5f), color),
				new VertexColor(new Vector3(+0.5f, +0.5f, -0.5f), color),
				new VertexColor(new Vector3(+0.5f, -0.5f, -0.5f), color),
				new VertexColor(new Vector3(+0.5f, -0.5f, +0.5f), color),
                // Back
                new VertexColor(new Vector3(+0.5f, +0.5f, -0.5f), color),
				new VertexColor(new Vector3(-0.5f, +0.5f, -0.5f), color),
				new VertexColor(new Vector3(-0.5f, -0.5f, -0.5f), color),
				new VertexColor(new Vector3(+0.5f, -0.5f, -0.5f), color),
                // Front
                new VertexColor(new Vector3(-0.5f, +0.5f, +0.5f), color),
				new VertexColor(new Vector3(+0.5f, +0.5f, +0.5f), color),
				new VertexColor(new Vector3(+0.5f, -0.5f, +0.5f), color),
				new VertexColor(new Vector3(-0.5f, -0.5f, +0.5f), color),
			};

			return vertices;
		}

		private static ushort[] GetCubeIndices()
		{
			ushort[] indices =
			{
				0,1,2, 0,2,3,
				4,5,6, 4,6,7,
				8,9,10, 8,10,11,
				12,13,14, 12,14,15,
				16,17,18, 16,18,19,
				20,21,22, 20,22,23,
			};

			return indices;
		}
	}
}