using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;

namespace vanhaodev.addressablesencryption.Tests
{
	/// <summary>The opener checks the AAEN header and the key, then streams the bundle plaintext (the stream itself is tested in encryptionmanager).</summary>
	public class EncryptedBundleStreamOpenerTests
	{
		private const string Secret = "opener-test";
		private static readonly byte[] Key = AddressablesEncryption.DeriveKey(Secret);

		private static byte[] Plain(int size)
		{
			var bytes = new byte[size];
			new Random(size).NextBytes(bytes);
			return bytes;
		}

		[TearDown]
		public void TearDown()
		{
			AddressablesEncryption.ClearKey();
		}

		[Test]
		public void OpenMemory_StreamsPlaintext()
		{
			AddressablesEncryption.SetKey(Secret);
			var plain = Plain(300000);
			using var stream = EncryptedBundleStreamOpener.OpenMemory(EncryptedBundleFormat.Encrypt(plain, Key), "bundle");

			Assert.AreEqual(plain.Length, stream.Length);
			var read = new byte[plain.Length];
			var total = 0;
			int n;
			while ((n = stream.Read(read, total, read.Length - total)) > 0)
				total += n;
			CollectionAssert.AreEqual(plain, read);
		}

		[Test]
		public void OpenFile_StreamsPlaintext()
		{
			AddressablesEncryption.SetKey(Secret);
			var plain = Plain(70000);
			var path = Path.GetTempFileName();
			try
			{
				File.WriteAllBytes(path, EncryptedBundleFormat.Encrypt(plain, Key));
				using var stream = EncryptedBundleStreamOpener.OpenFile(path, "bundle");
				stream.Seek(69990, SeekOrigin.Begin);
				var tail = new byte[10];
				Assert.AreEqual(10, stream.Read(tail, 0, 10));
				CollectionAssert.AreEqual(new ArraySegment<byte>(plain, 69990, 10), tail);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[Test]
		public void NoKey_Throws()
		{
			var encrypted = EncryptedBundleFormat.Encrypt(Plain(100), Key);
			var e = Assert.Throws<InvalidOperationException>(() => EncryptedBundleStreamOpener.OpenMemory(encrypted, "bundle"));
			StringAssert.Contains("No key set", e.Message);
		}

		[Test]
		public void WrongKey_Throws_NamingTheBundle()
		{
			AddressablesEncryption.SetKey("other-secret");
			var encrypted = EncryptedBundleFormat.Encrypt(Plain(5000), Key);
			var e = Assert.Throws<CryptographicException>(() => EncryptedBundleStreamOpener.OpenMemory(encrypted, "my-bundle"));
			StringAssert.Contains("wrong key", e.Message);
			StringAssert.Contains("my-bundle", e.Message);
		}

		[Test]
		public void NotEncrypted_AndTruncated_Throw()
		{
			AddressablesEncryption.SetKey(Secret);
			var plainBundle = Plain(200);
			var e = Assert.Throws<InvalidDataException>(() => EncryptedBundleStreamOpener.OpenMemory(plainBundle, "bundle"));
			StringAssert.Contains("not encrypted", e.Message);

			var encrypted = EncryptedBundleFormat.Encrypt(Plain(200), Key);
			var truncated = new byte[25];
			Buffer.BlockCopy(encrypted, 0, truncated, 0, 25);
			Assert.Throws<InvalidDataException>(() => EncryptedBundleStreamOpener.OpenMemory(truncated, "bundle"));
		}
	}
}
