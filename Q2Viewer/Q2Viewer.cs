using System;
using System.Numerics;
using Imagini;
using Imagini.Veldrid;
using Veldrid;

namespace Q2Viewer
{
	public class Q2Viewer : VeldridApp
	{
		private Camera _camera;
		private CommandList _cmd;

		// TODO: Read paths to PAKs to get textures from
		public Q2Viewer(string bspPath) : base(
			new WindowSettings()
			{
				WindowWidth = 800,
				WindowHeight = 600,
				IsResizable = true,
			}
		)
		{
			_camera = new Camera(800, 600);
			this.Resized += (s, e) =>
			{
				var windowSize = this.Window.Size;
				_camera.WindowResized(windowSize.Width, windowSize.Height);
			};
			InputTracker.Connect(this);
			// TODO: Read player_info_start entity from bsp
			_camera.Position = new Vector3(-100.0f, 0.0f, 0.0f);
		}

		protected override void Initialize()
		{
			var factory = Graphics.ResourceFactory;
			_cmd = factory.CreateCommandList();
		}

		protected override void Update(TimeSpan frameTime)
		{
			_camera.Update((float)frameTime.Milliseconds / 1000.0f);
			InputTracker.AfterUpdate();
		}

		protected override void Draw(TimeSpan frameTime)
		{
			_cmd.Begin();
			_cmd.SetFramebuffer(Graphics.SwapchainFramebuffer);
			_cmd.ClearColorTarget(0, RgbaFloat.CornflowerBlue);
			_cmd.End();
			Graphics.SubmitCommands(_cmd);
		}

		protected override void AfterDraw(TimeSpan frameTime)
		{
			Graphics.WaitForIdle();
			Graphics.SwapBuffers();
		}
	}
}