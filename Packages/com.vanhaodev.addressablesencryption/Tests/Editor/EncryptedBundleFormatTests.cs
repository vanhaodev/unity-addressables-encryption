using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace vanhaodev.addressablesencryption.Tests
{
	public class EncryptedBundleFormatTests
	{
		private static readonly byte[] PlainBundle = Encoding.ASCII.GetBytes("UnityFS\0fake bundle payload for tests");

		[TearDown]
		public void TearDown() => AddressablesEncryption.ClearKey();

		[Test]
		public void EncryptThenDecrypt_ReturnsOriginal()
		{
			var key = AddressablesEncryption.DeriveKey("secret");
			var encrypted = EncryptedBundleFormat.Encrypt(PlainBundle, key);

			AddressablesEncryption.SetKey("secret");
			var decrypted = AddressablesEncryption.DecryptBundle(encrypted, "test");

			Assert.That(decrypted, Is.EqualTo(PlainBundle));
		}

		[Test]
		public void Encrypted_HidesUnityFsHeader_AndHasMagic()
		{
			var encrypted = EncryptedBundleFormat.Encrypt(PlainBundle, AddressablesEncryption.DeriveKey("secret"));

			Assert.IsTrue(EncryptedBundleFormat.IsEncrypted(encrypted));
			Assert.IsFalse(EncryptedBundleFormat.IsEncrypted(PlainBundle));
			Assert.IsFalse(Encoding.ASCII.GetString(encrypted).Contains("UnityFS"));
		}

		[Test]
		public void SameSecret_SameKey_DifferentSecret_DifferentKey()
		{
			Assert.That(AddressablesEncryption.DeriveKey("a"), Is.EqualTo(AddressablesEncryption.DeriveKey("a")));
			Assert.That(AddressablesEncryption.DeriveKey("a"), Is.Not.EqualTo(AddressablesEncryption.DeriveKey("b")));
			Assert.AreEqual(32, AddressablesEncryption.DeriveKey("a").Length);
		}

		[Test]
		public void RawKey_RoundTrip()
		{
			var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
			var encrypted = EncryptedBundleFormat.Encrypt(PlainBundle, key);

			AddressablesEncryption.SetKey(key);
			Assert.That(AddressablesEncryption.DecryptBundle(encrypted, "test"), Is.EqualTo(PlainBundle));
		}

		[Test]
		public void Decrypt_WithoutKey_Throws()
		{
			var encrypted = EncryptedBundleFormat.Encrypt(PlainBundle, AddressablesEncryption.DeriveKey("secret"));
			Assert.Throws<InvalidOperationException>(() => AddressablesEncryption.DecryptBundle(encrypted, "test"));
		}

		[Test]
		public void Decrypt_PlainBundle_ThrowsInvalidData()
		{
			AddressablesEncryption.SetKey("secret");
			Assert.Throws<InvalidDataException>(() => AddressablesEncryption.DecryptBundle(PlainBundle, "test"));
		}

		[Test]
		public void Decrypt_TruncatedFile_ThrowsInvalidData()
		{
			var encrypted = EncryptedBundleFormat.Encrypt(PlainBundle, AddressablesEncryption.DeriveKey("secret"));
			AddressablesEncryption.SetKey("secret");
			Assert.Throws<InvalidDataException>(() => AddressablesEncryption.DecryptBundle(encrypted.Take(10).ToArray(), "test"));
		}

		[Test]
		public void Decrypt_WrongKey_DoesNotReturnOriginal()
		{
			var encrypted = EncryptedBundleFormat.Encrypt(PlainBundle, AddressablesEncryption.DeriveKey("right"));
			AddressablesEncryption.SetKey("wrong");

			// CBC with a wrong key almost always fails the padding check; if it passes, the output is garbage.
			try
			{
				Assert.That(AddressablesEncryption.DecryptBundle(encrypted, "test"), Is.Not.EqualTo(PlainBundle));
			}
			catch (CryptographicException)
			{
				Assert.Pass();
			}
		}

		[Test]
		public void SetKey_InvalidRawKeySize_Throws()
		{
			Assert.Throws<ArgumentException>(() => AddressablesEncryption.SetKey(new byte[10]));
			Assert.Throws<ArgumentException>(() => AddressablesEncryption.SetKey(""));
		}
	}
}
