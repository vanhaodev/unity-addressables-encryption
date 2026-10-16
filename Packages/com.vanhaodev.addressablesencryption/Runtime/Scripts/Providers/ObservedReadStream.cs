#if UNITY_EDITOR
using System;
using System.IO;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Pass-through stream that reports each read to <see cref="AddressablesEncryption.BundleStreamRead"/>.
	/// Editor only, and only while someone listens; otherwise the decrypting stream is handed to Unity directly.
	/// </summary>
	internal sealed class ObservedReadStream : Stream
	{
		private readonly Stream _inner;
		private readonly string _bundleId;

		public ObservedReadStream(Stream inner, string bundleId)
		{
			_inner = inner;
			_bundleId = bundleId;
		}

		public override bool CanRead => _inner.CanRead;
		public override bool CanSeek => _inner.CanSeek;
		public override bool CanWrite => false;
		public override long Length => _inner.Length;

		public override long Position
		{
			get => _inner.Position;
			set => _inner.Position = value;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			var position = _inner.Position;
			var read = _inner.Read(buffer, offset, count);
			if (read > 0)
			{
				try
				{
					AddressablesEncryption.RaiseBundleStreamRead(_bundleId, position, read, _inner.Length);
				}
				catch (Exception)
				{
					// A faulty debug listener must never break loading.
				}
			}
			return read;
		}

		public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
		public override void Flush() { }
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_inner.Dispose();
			base.Dispose(disposing);
		}
	}
}
#endif
