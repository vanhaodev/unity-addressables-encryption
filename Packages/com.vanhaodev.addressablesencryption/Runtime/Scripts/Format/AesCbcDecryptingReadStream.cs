using System;
using System.IO;
using System.Security.Cryptography;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Read-only, seekable view of the plaintext inside an encrypted bundle file. Blocks are decrypted on demand
	/// in 128 KB chunks, so a bundle of any size costs about 256 KB of managed memory while it is loaded.<br/>
	/// Hand it to <c>AssetBundle.LoadFromStream(Async)</c> and keep it open until the bundle is unloaded.
	/// Unity may call Read/Seek from its loader thread: every public member takes one lock, no Unity API inside.<br/>
	/// Opening validates header, version, length and the PKCS7 padding of the last block, so a wrong key fails
	/// here (with ~255/256 odds) instead of producing garbage.
	/// </summary>
	internal sealed class AesCbcDecryptingReadStream : Stream
	{
		private const int BlockSize = AesCbcBlockMath.BlockSize;
		private const int BufferSize = 8192 * BlockSize; // 128 KB of ciphertext per refill

		private readonly object _lock = new object();
		private readonly bool _ownsSource;
		private readonly byte[] _iv = new byte[AesCbcBlockMath.IvSize];
		private readonly long _cipherLength;
		private readonly long _plainLength;

		private Stream _source;
		private Aes _aes;
		private ICryptoTransform _ecbDecryptor; // reused for every chunk: ECB has no chaining state
		private byte[] _key;
		// Ciphertext chunk with the previous block (the IV of the chunk) in front of it, and its decrypted form.
		private byte[] _cipherBuffer;
		private byte[] _plainBuffer;
		private long _bufferStart = -1; // plaintext offset of _plainBuffer[0]
		private int _bufferLength;
		private long _position;

		/// <param name="key">Raw AES key. The stream owns it and clears it on Dispose.</param>
		/// <param name="ownsSource">Dispose <paramref name="source"/> together with this stream.</param>
		public AesCbcDecryptingReadStream(Stream source, byte[] key, string bundleId, bool ownsSource)
		{
			_source = source;
			_ownsSource = ownsSource;
			_key = key;
			// CBC decryption done by hand: ECB-decrypt each block, then XOR with the previous ciphertext block.
			// One ECB transform serves every chunk (no IV state), so refills allocate nothing.
			_aes = Aes.Create();
			_aes.Mode = CipherMode.ECB;
			_aes.Padding = PaddingMode.None; // padding is handled by hand (Length), blocks are decrypted raw
			_aes.Key = key;
			_ecbDecryptor = _aes.CreateDecryptor();

			var header = new byte[EncryptedBundleFormat.HeaderSize];
			ReadExactly(0, header, 0, header.Length, bundleId);
			EncryptedBundleFormat.ValidateHeader(header, bundleId);

			_cipherLength = AesCbcBlockMath.CipherLength(source.Length, bundleId);
			ReadExactly(EncryptedBundleFormat.HeaderSize, _iv, 0, _iv.Length, bundleId);
			_plainLength = _cipherLength - ReadPaddingLength(bundleId);
		}

		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => _plainLength;

		public override long Position
		{
			get { lock (_lock) return _position; }
			set { Seek(value, SeekOrigin.Begin); }
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			lock (_lock)
			{
				ThrowIfDisposed();
				if (count <= 0 || _position >= _plainLength)
					return 0;

				var remaining = _plainLength - _position;
				if (count > remaining)
					count = (int)remaining;

				var total = 0;
				while (count > 0)
				{
					if (_position < _bufferStart || _position >= _bufferStart + _bufferLength)
						Refill(_position);

					var inBuffer = (int)(_position - _bufferStart);
					var n = Math.Min(count, _bufferLength - inBuffer);
					Buffer.BlockCopy(_plainBuffer, inBuffer, buffer, offset + total, n);
					total += n;
					count -= n;
					_position += n;
				}
				return total;
			}
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			lock (_lock)
			{
				ThrowIfDisposed();
				var target = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : _plainLength + offset;
				if (target < 0 || target > _plainLength)
					throw new IOException($"[AddressablesEncryption] Seek outside the bundle (position {target}, length {_plainLength}).");
				_position = target;
				return _position;
			}
		}

		public override void Flush() { }
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			lock (_lock)
			{
				if (_source == null)
					return;
				if (_ownsSource)
					_source.Dispose();
				_source = null;
				_ecbDecryptor.Dispose();
				_ecbDecryptor = null;
				_aes.Dispose();
				_aes = null;
				Array.Clear(_key, 0, _key.Length);
				_key = null;
				_cipherBuffer = null;
				_plainBuffer = null;
			}
			base.Dispose(disposing);
		}

		/// <summary>Decrypt the chunk that contains plaintext offset <paramref name="position"/> into the buffers.</summary>
		private void Refill(long position)
		{
			_cipherBuffer ??= new byte[BufferSize + BlockSize];
			_plainBuffer ??= new byte[BufferSize];

			var blockIndex = position / BlockSize;
			var start = blockIndex * BlockSize;
			var count = (int)Math.Min(BufferSize, _cipherLength - start);

			if (blockIndex == 0)
			{
				Buffer.BlockCopy(_iv, 0, _cipherBuffer, 0, BlockSize);
				ReadExactly(AesCbcBlockMath.CipherOffset(0), _cipherBuffer, BlockSize, count, null);
			}
			else
			{
				// One read: previous block (chaining IV) followed by the chunk itself.
				ReadExactly(AesCbcBlockMath.CipherOffset(blockIndex - 1), _cipherBuffer, 0, count + BlockSize, null);
			}

			DecryptBlocks(_cipherBuffer, count, _plainBuffer);
			_bufferStart = start;
			_bufferLength = (int)Math.Min(count, _plainLength - start); // last chunk: drop the padding
		}

		/// <summary>
		/// CBC-decrypt <paramref name="count"/> bytes of <paramref name="cipher"/> starting at index 16, where
		/// cipher[0..16] is the previous ciphertext block (the chaining IV): plain_i = ECB_dec(C_i) XOR C_(i-1).
		/// </summary>
		private void DecryptBlocks(byte[] cipher, int count, byte[] plain)
		{
			var done = _ecbDecryptor.TransformBlock(cipher, BlockSize, count, plain, 0);
			if (done < count)
			{
				// Not expected for ECB without padding, but some implementations hold back a block: flush it and
				// start a fresh transform, since a finalized one cannot be reused.
				var rest = _ecbDecryptor.TransformFinalBlock(cipher, BlockSize + done, count - done);
				Buffer.BlockCopy(rest, 0, plain, done, rest.Length);
				_ecbDecryptor.Dispose();
				_ecbDecryptor = _aes.CreateDecryptor();
			}

			// plain[i] holds ECB_dec(C_i); the previous ciphertext block sits 16 bytes earlier in 'cipher'.
			for (int i = 0; i < count; i++)
				plain[i] ^= cipher[i];
		}

		/// <summary>Decrypt only the last block to read its PKCS7 padding. Throws on a wrong key.</summary>
		private int ReadPaddingLength(string bundleId)
		{
			var lastIndex = _cipherLength / BlockSize - 1;
			var cipher = new byte[2 * BlockSize];
			var plain = new byte[BlockSize];
			if (lastIndex == 0)
			{
				Buffer.BlockCopy(_iv, 0, cipher, 0, BlockSize);
				ReadExactly(AesCbcBlockMath.CipherOffset(0), cipher, BlockSize, BlockSize, bundleId);
			}
			else
			{
				ReadExactly(AesCbcBlockMath.CipherOffset(lastIndex - 1), cipher, 0, 2 * BlockSize, bundleId);
			}
			DecryptBlocks(cipher, BlockSize, plain);
			return AesCbcBlockMath.PaddingLength(plain, bundleId);
		}

		private void ReadExactly(long fileOffset, byte[] buffer, int offset, int count, string bundleId)
		{
			_source.Seek(fileOffset, SeekOrigin.Begin);
			while (count > 0)
			{
				var n = _source.Read(buffer, offset, count);
				if (n <= 0)
					throw new EndOfStreamException($"[AddressablesEncryption] Bundle '{bundleId ?? "stream"}' ended early at {fileOffset}: truncated file.");
				offset += n;
				count -= n;
			}
		}

		private void ThrowIfDisposed()
		{
			if (_source == null)
				throw new ObjectDisposedException(nameof(AesCbcDecryptingReadStream));
		}
	}
}
