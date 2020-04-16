using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Imagini;
using Imagini.Veldrid;
using SharpFileSystem;
using SharpFileSystem.FileSystems;
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
		private ModelRenderer _modelRenderer;
		private BSPRenderer _renderer;
		private readonly Options _options;
		private IFileSystem _fs;

		private bool _showWireframe = false;
		private bool _showColored = false;
		private bool _showGizmo = false;


		public Q2Viewer(Options options) : base(
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
			_fs = MountFilesystem(options);
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

		private IFileSystem MountFilesystem(Options options)
		{
			if (options.PakPaths == null || !options.PakPaths.Any())
				return new MemoryFileSystem();
			var paks = options.PakPaths
				.Select(System.IO.File.OpenRead)
				.Select(s => new QPakFS(s));
			return new MergedFileSystem(paks);
		}

		protected override void Initialize()
		{
			var factory = Graphics.ResourceFactory;
			_cl = factory.CreateCommandList();
			_viewBuf = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_projBuf = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
			_debugPrimitives = new DebugPrimitives(Graphics, _viewBuf, _projBuf, _camera);
			_modelRenderer = new ModelRenderer(Graphics, _viewBuf, _projBuf, _camera);

			var bspFileStream = System.IO.File.OpenRead(_options.MapPath);
			var bspFile = new BSPFile(bspFileStream, SharedArrayPoolAllocator.Instance);
			bspFileStream.Close();
			_renderer = new BSPRenderer(bspFile, SharedArrayPoolAllocator.Instance, Graphics, _fs);
		}

		private float _animTime = 0f;
		protected override void Update(TimeSpan frameTime)
		{
			var deltaSeconds = (float)frameTime.Ticks / TimeSpan.TicksPerSecond;
			_camera.Update(deltaSeconds);

			if (InputTracker.IsKeyTriggered(Keycode.NUMBER_1))
				_showWireframe = !_showWireframe;
			if (InputTracker.IsKeyTriggered(Keycode.NUMBER_2))
				_showColored = !_showColored;
			if (InputTracker.IsKeyTriggered(Keycode.NUMBER_3))
				_showGizmo = !_showGizmo;

			InputTracker.AfterUpdate();
			_animTime += deltaSeconds;
			if (_animTime >= 0.01f)
			{
				_animTime -= 0.01f;
				_modelRenderer.NextAnimationFrame();
			}
		}

		protected override void Draw(TimeSpan frameTime)
		{
			_cl.Begin();
			_cl.SetFramebuffer(Graphics.SwapchainFramebuffer);
			_cl.ClearColorTarget(0, RgbaFloat.CornflowerBlue);
			_cl.ClearDepthStencil(1.0f);
			_cl.UpdateBuffer(_viewBuf, 0, _camera.ViewMatrix);
			_cl.UpdateBuffer(_projBuf, 0, _camera.ProjectionMatrix);

			var calls = 0;
			if (_showGizmo)
			{
				_debugPrimitives.DrawGizmo(_cl); calls++;
			}

			if (_showColored)
				calls += _renderer.DrawDebugModels(_cl, _debugPrimitives);
			else
				calls += _renderer.Draw(_cl, _modelRenderer);

			if (_showWireframe)
				calls += _renderer.DrawWireframe(_cl, _debugPrimitives);
			_cl.End();
			Graphics.SubmitCommands(_cl);

			Window.Title = $"Q2Viewer (draw calls: {calls})";
		}

		protected override void AfterDraw(TimeSpan frameTime)
		{
			Graphics.SwapBuffers(Graphics.MainSwapchain);
			Graphics.WaitForIdle();
		}
	}
}