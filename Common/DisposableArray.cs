using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Common
{
	public unsafe struct DisposableArray<T> : IDisposable
		where T : unmanaged
	{
		private readonly T* _pointer;
		private readonly int _length;
		private readonly IMemoryAllocator _allocator;

		public int Length => _length;

		public static readonly DisposableArray<T> Null = new DisposableArray<T>(0);

		public T this[int index]
		{
			get
			{
				if (index < 0 || index >= _length)
					throw new IndexOutOfRangeException();
				return *(_pointer + index);
			}
			set
			{
				if (index < 0 || index >= _length)
					throw new IndexOutOfRangeException();
				*(_pointer + index) = value;
			}
		}

		public bool IsNull() => _pointer == null;
		public bool IsDisposed() => _isDisposed;

		public DisposableArray(int count, IMemoryAllocator allocator)
		{
			_pointer = allocator.Allocate<T>(count);
			_length = count;
			_allocator = allocator;
			_isDisposed = false;
		}

		private bool _isDisposed;
		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			_allocator.Free(_pointer);
		}


		public static implicit operator Span<T>(DisposableArray<T> array)
		{
			if (array._pointer == null)
				throw new NullReferenceException($"Attempt to access an unitialized {nameof(DisposableArray<T>)}");
			if (array._isDisposed)
				throw new AccessViolationException($"Attempt to access a disposed ${nameof(DisposableArray<T>)}");
			return new Span<T>(array._pointer, array._length);
		}

		public Span<T> AsSpan() => (Span<T>)this;

		public ReadOnlySpan<T> AsReadOnlySpan() => (ReadOnlySpan<T>)(Span<T>)this;

		private DisposableArray(int unused)
		{
			_pointer = null;
			_length = 0;
			_allocator = null;
			_isDisposed = true;
		}
	}

	// note: it's not calling Dispose on scope end for some reason
	// I'll leave it here for historic purposes (it's currently unused)
	public ref struct ScopedArray<T>
		where T : unmanaged
	{
		private DisposableArray<T> _instance;

		public ScopedArray(DisposableArray<T> instance) =>
			_instance = instance;

		public void Dispose() => _instance.Dispose();

		public static implicit operator Span<T>(ScopedArray<T> array) =>
			array._instance;

		public T this[int index]
		{
			get => _instance[index];
			set => _instance[index] = value;
		}

		public Span<T> AsSpan() => (Span<T>)this;

		public ReadOnlySpan<T> AsReadOnlySpan() => (ReadOnlySpan<T>)(Span<T>)this;
	}
}