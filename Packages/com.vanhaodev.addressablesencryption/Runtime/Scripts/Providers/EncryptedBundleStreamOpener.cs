using System.IO;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Opens a decrypting stream over an encrypted bundle, on disk or in memory, with the current key.
	/// Throws the descriptive errors of <see cref="AesCbcDecryptingReadStream"/> (no key, wrong key, not encrypted, truncated).
	/// </summary>
	internal static class EncryptedBundleStreamOpener
	{
		private const int FileBufferSize = 4096;

		/// <summary>Local or cached bundle file. FileShare.Delete lets the cache replace the file while it is open.</summary>
		public static AesCbcDecryptingReadStream OpenFile(string path, string bundleId)
		{
			var key = AddressablesEncryption.CurrentKeyOrThrow(bundleId);
			var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, FileBufferSize, FileOptions.RandomAccess);
			try
			{
				return new AesCbcDecryptingReadStream(file, key, bundleId, ownsSource: true);
			}
			catch
			{
				file.Dispose();
				throw;
			}
		}

		/// <summary>Downloaded bundle that could not go to the cache (group without "Use Asset Bundle Cache").</summary>
		public static AesCbcDecryptingReadStream OpenMemory(byte[] encryptedBytes, string bundleId)
		{
			var key = AddressablesEncryption.CurrentKeyOrThrow(bundleId);
			return new AesCbcDecryptingReadStream(new MemoryStream(encryptedBytes, writable: false), key, bundleId, ownsSource: true);
		}
	}
}
