using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Imagini;
using Imagini.Veldrid;
using Veldrid;

namespace Q2Viewer
{
	public class Q2Viewer : VeldridApp
	{
		private readonly Camera _camera;
		private CommandList _cl;
		/* View and projection matrices */
		private DeviceBuffer _viewBuf;
		private DeviceBuffer _projBuf;
		private DebugPrimitives _debugPrimitives;
		private BSPRenderer _renderer;
		private readonly Options _options;


		// TODO: Read paths to PAKs to get textures from
		public Q2Viewer(Options options) : base(
			new WindowSettings()
			{
				WindowWidth = 800,
				WindowHeight = 600,
				IsResizable = true,
			},
			new GraphicsDeviceOptions(
				debug: false,
				swapchainDepthFormat: PixelFormat.R16_UNorm,
				syncToVerticalBlank: true,
				resourceBindingModel: ResourceBindingModel.Improved,
				preferDepthRangeZeroToOne: true,
				preferStandardClipSpaceYDirection: true
			)
		)
		{
			_options = options;
			_camera = new Camera(800, 600);
			this.Resized += (s, e) =>
			{
				var windowSize = this.Window.Size;
				_camera.WindowResized(windowSize.Width, windowSize.Height);
			};
			InputTracker.Connect(this);
			// TODO: Read player_info_start entity from bsp
			_camera.Position = new Vector3(25.0f, 15.0f, 25.0f);
			_camera.Yaw = MathF.PI / 4;
			_camera.Pitch = -MathF.PI / 8;
		}

		protected override void Initialize()
		{
			var factory = Graphics.ResourceFactory;
			_cl = factory.CreateCommandList();
			_viewBuf = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_projBuf = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_debugPrimitives = new DebugPrimitives(Graphics, _viewBuf, _projBuf, _camera);

			var bspFileStream = File.OpenRead(_options.MapPath);
			var bspFile = new BSPFile(bspFileStream, SharedArrayPoolAllocator.Instance);
			bspFileStream.Close();
			_renderer = new BSPRenderer(bspFile, SharedArrayPoolAllocator.Instance, Graphics);
		}

		protected override void Update(TimeSpan frameTime)
		{
			_camera.Update((float)frameTime.Milliseconds / 1000.0f);
			InputTracker.AfterUpdate();
		}

		protected override void Draw(TimeSpan frameTime)
		{
			_cl.Begin();
			_cl.SetFramebuffer(Graphics.SwapchainFramebuffer);
			_cl.ClearColorTarget(0, RgbaFloat.CornflowerBlue);
			_cl.ClearDepthStencil(1.0f);
			_cl.UpdateBuffer(_viewBuf, 0, _camera.ViewMatrix);
			_cl.UpdateBuffer(_projBuf, 0, _camera.ProjectionMatrix);
			_debugPrimitives.DrawGizmo(_cl);
			_debugPrimitives.DrawCube(_cl, Vector3.Zero);
			_renderer.DrawWireframe(_cl, _debugPrimitives);
			_cl.End();
			Graphics.SubmitCommands(_cl);
		}

		protected override void AfterDraw(TimeSpan frameTime)
		{
			Graphics.SwapBuffers(Graphics.MainSwapchain);
			Graphics.WaitForIdle();
		}
	}
}