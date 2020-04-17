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
	}
}