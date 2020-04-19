using System;
using System.Numerics;
using Common;
using Imagini;
using Imagini.Veldrid;
using SharpFileSystem;
using Veldrid;

namespace MD2Viewer
{
	public class MD2Viewer : VeldridApp
	{
		private readonly Camera _camera;
		private readonly Options _options;
		private IFileSystem _fs;
		private CommandList _cl;

		private DeviceBuffer _viewBuf;
		private DeviceBuffer _projBuf;
		private MD2Renderer _renderer;

		public MD2Viewer(Options options) : base(
			new WindowSettings()
			{
				WindowWidth = 800,
				WindowHeight = 600,
				IsResizable = true,
				VSync = true,
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
			_fs = QPakFS.MountFilesystem(options.PakPaths);
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
			var rf = Graphics.ResourceFactory;
			_cl = rf.CreateCommandList();
			_viewBuf = rf.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_projBuf = rf.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

			var md2FileStream = System.IO.File.OpenRead(_options.ModelPath);
			var md2File = new MD2File(md2FileStream, SharedArrayPoolAllocator.Instance);
			_renderer = new MD2Renderer(
				Graphics,
				_fs,
				md2File,
				SharedArrayPoolAllocator.Instance,
				_viewBuf,
				_projBuf,
				_camera);
		}

		protected override void Update(TimeSpan frameTime)
		{
			var deltaSeconds = (float)frameTime.Ticks / TimeSpan.TicksPerSecond;
			_camera.Update(deltaSeconds);

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
			_renderer.Draw(_cl, Matrix4x4.Identity);
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