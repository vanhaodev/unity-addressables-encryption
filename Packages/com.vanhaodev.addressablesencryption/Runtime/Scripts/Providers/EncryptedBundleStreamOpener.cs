using System;
using System.IO;
using System.Security.Cryptography;
using vanhaodev.encryptionmanager;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Opens a decrypting stream over an encrypted bundle (on disk or in memory) with the current key:
	/// checks the AAEN header, then hands the IV + ciphertext part to <see cref="AesEncryption.OpenDecryptStream(Stream, long, bool)"/>.
	/// Throws descriptive errors: no key, wrong key, not encrypted, unsupported version, truncated.
	/// </summary>
	internal static class EncryptedBundleStreamOpener
	{
		private const int FileBufferSize = 4096;

		/// <summary>Local or cached bundle file. FileShare.Delete lets the cache replace the file while it is open.</summary>
		public static Stream OpenFile(string path, string bundleId)
		{
			var key = AddressablesEncryption.CurrentKeyOrThrow(bundleId);
			var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, FileBufferSize, FileOptions.RandomAccess);
			try
			{
				return Open(file, key, bundleId);
			}
			catch
			{
				file.Dispose();
				throw;
			}
		}

		/// <summary>Downloaded bundle that could not go to the cache (group without "Use Asset Bundle Cache").</summary>
		public static Stream OpenMemory(byte[] encryptedBytes, string bundleId)
		{
			var key = AddressablesEncryption.CurrentKeyOrThrow(bundleId);
			return Open(new MemoryStream(encryptedBytes, writable: false), key, bundleId);
		}

		private static Stream Open(Stream source, byte[] key, string bundleId)
		{
			try
			{
				var header = new byte[EncryptedBundleFormat.HeaderSize];
				var read = 0;
				while (read < header.Length)
				{
					var n = source.Read(header, read, header.Length - read);
					if (n <= 0)
						break;
					read += n;
				}
				if (read < header.Length)
					throw new InvalidDataException($"[AddressablesEncryption] Bundle '{bundleId}' is truncated ({source.Length} bytes).");
				EncryptedBundleFormat.ValidateHeader(header, bundleId);

				// The stream keeps its own copy of the key; the AesEncryption wrapper is discarded right away.
				var plain = new AesEncryption(key).OpenDecryptStream(source, EncryptedBundleFormat.HeaderSize, ownsSource: true);
#if UNITY_EDITOR
				if (AddressablesEncryption.HasBundleStreamReadListener)
					return new ObservedReadStream(plain, bundleId);
#endif
				return plain;
			}
			catch (CryptographicException e)
			{
				throw new CryptographicException($"[AddressablesEncryption] Cannot decrypt bundle '{bundleId}': wrong key or corrupted file.", e);
			}
			catch (InvalidDataException e) when (!e.Message.StartsWith("[AddressablesEncryption]", StringComparison.Ordinal))
			{
				throw new InvalidDataException($"[AddressablesEncryption] Bundle '{bundleId}' is truncated or damaged: {e.Message}", e);
			}
			finally
			{
				Array.Clear(key, 0, key.Length);
			}
		}
	}
}
