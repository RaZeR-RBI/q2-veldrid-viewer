using System.Collections.Generic;
using System.Numerics;
using Imagini;

namespace Common
{
	public static class InputTracker
	{
		public static Vector2 MousePosition { get; private set; }

		public static Vector2 MouseDelta { get; private set; }

		private static HashSet<Keycode> _previousKeys = new HashSet<Keycode>(10);
		private static HashSet<Keycode> _pressedKeys = new HashSet<Keycode>(10);
		private static HashSet<MouseButton> _mouseButtons = new HashSet<MouseButton>(10);


		public static void Connect(AppBase app)
		{
			app.Events.Keyboard.KeyPressed += OnKeyPressed;
			app.Events.Keyboard.KeyReleased += OnKeyReleased;
			app.Events.Mouse.MouseMoved += OnMouseMoved;
			app.Events.Mouse.MouseButtonPressed += OnMouseButtonPressed;
			app.Events.Mouse.MouseButtonReleased += OnMouseButtonReleased;
		}

		public static void Disconnect(AppBase app)
		{
			app.Events.Keyboard.KeyPressed -= OnKeyPressed;
			app.Events.Keyboard.KeyReleased -= OnKeyReleased;
			app.Events.Mouse.MouseMoved -= OnMouseMoved;
			app.Events.Mouse.MouseButtonPressed -= OnMouseButtonPressed;
			app.Events.Mouse.MouseButtonReleased -= OnMouseButtonReleased;
		}

		public static void AfterUpdate()
		{
			MouseDelta = Vector2.Zero;
			_previousKeys.RemoveWhere(k => !_pressedKeys.Contains(k));
			foreach (var key in _pressedKeys)
				_previousKeys.Add(key);
		}

		public static bool IsKeyTriggered(Keycode keycode) =>
			_pressedKeys.Contains(keycode) && !_previousKeys.Contains(keycode);

		public static bool GetKey(Keycode keycode) => _pressedKeys.Contains(keycode);

		public static bool GetMouseButton(MouseButton b) => _mouseButtons.Contains(b);

		private static void OnKeyPressed(object sender, KeyboardEventArgs args) =>
			_pressedKeys.Add(args.Key.Keycode);

		private static void OnKeyReleased(object sender, KeyboardEventArgs args) =>
			_pressedKeys.Remove(args.Key.Keycode);

		private static void OnMouseMoved(object sender, MouseMoveEventArgs args)
		{
			MousePosition = new Vector2(args.X, args.Y);
			MouseDelta = new Vector2(args.X, args.Y);
		}

		private static void OnMouseButtonPressed(object sender, MouseButtonEventArgs args) =>
			_mouseButtons.Add(args.Button);

		private static void OnMouseButtonReleased(object sender, MouseButtonEventArgs args) =>
			_mouseButtons.Remove(args.Button);
	}
}