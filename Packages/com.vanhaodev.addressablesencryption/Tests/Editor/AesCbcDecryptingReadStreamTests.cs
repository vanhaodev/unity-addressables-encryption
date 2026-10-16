using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;

namespace vanhaodev.addressablesencryption.Tests
{
	/// <summary>The decrypting stream must give exactly the original plaintext, for any size and any seek.</summary>
	public class AesCbcDecryptingReadStreamTests
	{
		private static readonly byte[] Key = AddressablesEncryption.DeriveKey("stream-test");
		private static readonly int[] Sizes = { 1, 15, 16, 17, 4095, 4096, 65536, 65537, 131072, 131073, 300000 };

		private static byte[] Plain(int size, int seed)
		{
			var bytes = new byte[size];
			new Random(seed).NextBytes(bytes);
			return bytes;
		}

		private static AesCbcDecryptingReadStream Open(byte[] encrypted, byte[] key = null)
		{
			return new AesCbcDecryptingReadStream(new MemoryStream(encrypted, false), (byte[])(key ?? Key).Clone(), "test", true);
		}

		[Test]
		public void SequentialRead_MatchesPlaintext([ValueSource(nameof(Sizes))] int size)
		{
			var plain = Plain(size, size);
			using var stream = Open(EncryptedBundleFormat.Encrypt(plain, Key));

			Assert.AreEqual(size, stream.Length);
			var read = new byte[size];
			var total = 0;
			int n;
			while ((n = stream.Read(read, total, Math.Min(7919, size - total))) > 0) // odd chunk size crosses block edges
				total += n;
			Assert.AreEqual(size, total);
			CollectionAssert.AreEqual(plain, read);
			Assert.AreEqual(0, stream.Read(read, 0, 10), "read at end returns 0");
		}

		[Test]
		public void RandomSeekAndRead_MatchesPlaintext([ValueSource(nameof(Sizes))] int size)
		{
			var plain = Plain(size, size + 1);
			using var stream = Open(EncryptedBundleFormat.Encrypt(plain, Key));
			var random = new Random(42);
			var buffer = new byte[200000];

			for (int i = 0; i < 300; i++)
			{
				var start = random.Next(size + 1);
				var count = random.Next(Math.Min(buffer.Length, size - start) + 1);
				stream.Seek(start, SeekOrigin.Begin);
				var n = stream.Read(buffer, 0, count);
				Assert.AreEqual(count, n, $"iteration {i}: start {start} count {count}");
				for (int j = 0; j < n; j++)
					if (buffer[j] != plain[start + j])
						Assert.Fail($"iteration {i}: byte {start + j} differs");
				Assert.AreEqual(start + n, stream.Position);
			}
		}

		[Test]
		public void SeekOrigins_Work()
		{
			var plain = Plain(1000, 7);
			using var stream = Open(EncryptedBundleFormat.Encrypt(plain, Key));
			Assert.AreEqual(990, stream.Seek(-10, SeekOrigin.End));
			Assert.AreEqual(995, stream.Seek(5, SeekOrigin.Current));
			Assert.AreEqual(plain[995], stream.ReadByte());
			stream.Position = 3;
			Assert.AreEqual(plain[3], stream.ReadByte());
			Assert.Throws<IOException>(() => stream.Seek(1001, SeekOrigin.Begin));
			Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
		}

		[Test]
		public void WrongKey_ThrowsAtOpen()
		{
			var encrypted = EncryptedBundleFormat.Encrypt(Plain(5000, 1), Key);
			var wrong = AddressablesEncryption.DeriveKey("other");
			var e = Assert.Throws<CryptographicException>(() => Open(encrypted, wrong).Dispose());
			StringAssert.Contains("wrong key", e.Message);
		}

		[Test]
		public void BadHeader_Truncated_BadVersion_Throw()
		{
			var encrypted = EncryptedBundleFormat.Encrypt(Plain(100, 2), Key);

			var plainFile = new byte[encrypted.Length];
			Assert.Throws<InvalidDataException>(() => Open(plainFile).Dispose(), "not encrypted");

			var truncated = new byte[20];
			Buffer.BlockCopy(encrypted, 0, truncated, 0, 20);
			Assert.Throws<InvalidDataException>(() => Open(truncated).Dispose(), "truncated");

			var oddLength = new byte[encrypted.Length - 1];
			Buffer.BlockCopy(encrypted, 0, oddLength, 0, oddLength.Length);
			Assert.Throws<InvalidDataException>(() => Open(oddLength).Dispose(), "not a multiple of 16");

			var badVersion = (byte[])encrypted.Clone();
			badVersion[4] = 9;
			var e = Assert.Throws<InvalidDataException>(() => Open(badVersion).Dispose());
			StringAssert.Contains("format version", e.Message);
		}

		[Test]
		public void Dispose_ClearsKeyAndClosesSource()
		{
			var key = (byte[])Key.Clone();
			var source = new MemoryStream(EncryptedBundleFormat.Encrypt(Plain(64, 3), Key), false);
			var stream = new AesCbcDecryptingReadStream(source, key, "test", true);
			stream.Dispose();
			Assert.IsFalse(source.CanRead, "source closed");
			Assert.That(key, Is.All.EqualTo(0), "key cleared");
			Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[1], 0, 1));
		}
	}
}
