using System;
using System.IO;
using System.Security.Cryptography;
using vanhaodev.encryptionmanager;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// On-disk layout of an encrypted bundle:<br/>
	/// [magic "AAEN" 4B][format version 1B][AesEncryption output: IV 16B + AES-CBC ciphertext]<br/>
	/// The magic tells encrypted files apart from plain bundles (which start with "UnityFS"),
	/// so a build never encrypts a file twice and loading reports a clear error.
	/// </summary>
	public static class EncryptedBundleFormat
	{
		private static readonly byte[] Magic = { (byte)'A', (byte)'A', (byte)'E', (byte)'N' };
		internal const byte FormatVersion = 1;
		/// <summary>Magic (4) + format version (1).</summary>
		internal const int HeaderSize = 5;

		/// <summary>True if the bytes start with the encrypted bundle header.</summary>
		public static bool IsEncrypted(byte[] data)
		{
			if (data == null || data.Length < HeaderSize)
				return false;
			for (int i = 0; i < Magic.Length; i++)
			{
				if (data[i] != Magic[i])
					return false;
			}
			return true;
		}

		/// <summary>True if the file starts with the encrypted bundle header. False for missing or short files.</summary>
		public static bool IsEncryptedFile(string path)
		{
			try
			{
				using var file = File.OpenRead(path);
				var header = new byte[HeaderSize];
				return file.Read(header, 0, HeaderSize) == HeaderSize && IsEncrypted(header);
			}
			catch (IOException)
			{
				return false;
			}
		}

		/// <summary>Throws the same descriptive errors as <see cref="Decrypt"/> for a bad magic or version.</summary>
		internal static void ValidateHeader(byte[] header, string bundleId)
		{
			if (!IsEncrypted(header))
				throw new InvalidDataException(
					$"[AddressablesEncryption] Bundle '{bundleId}' is not encrypted or is corrupted (missing header). Rebuild Addressables with the encrypted build script.");
			if (header[Magic.Length] != FormatVersion)
				throw new InvalidDataException(
					$"[AddressablesEncryption] Bundle '{bundleId}' uses unsupported format version {header[Magic.Length]}.");
		}

		/// <summary>Encrypt a plain bundle file. Returns header + encrypted payload.</summary>
		public static byte[] Encrypt(byte[] bundleBytes, byte[] key)
		{
			AddressablesEncryption.ValidateRawKey(key);
			var payload = new AesEncryption(key).Encrypt(bundleBytes);

			var result = new byte[HeaderSize + payload.Length];
			Buffer.BlockCopy(Magic, 0, result, 0, Magic.Length);
			result[Magic.Length] = FormatVersion;
			Buffer.BlockCopy(payload, 0, result, HeaderSize, payload.Length);
			return result;
		}

		/// <summary>Decrypt an encrypted bundle file. Throws a descriptive exception on any mismatch.</summary>
		internal static byte[] Decrypt(byte[] fileBytes, AesEncryption aes, string bundleId)
		{
			if (fileBytes == null || fileBytes.Length < HeaderSize)
				throw new InvalidDataException(
					$"[AddressablesEncryption] Bundle '{bundleId}' is not encrypted or is corrupted (missing header). Rebuild Addressables with the encrypted build script.");
			ValidateHeader(fileBytes, bundleId);

			// IV (16) + at least one AES block (16)
			if (fileBytes.Length < HeaderSize + 32)
				throw new InvalidDataException($"[AddressablesEncryption] Bundle '{bundleId}' is truncated ({fileBytes.Length} bytes).");

			var payload = new byte[fileBytes.Length - HeaderSize];
			Buffer.BlockCopy(fileBytes, HeaderSize, payload, 0, payload.Length);

			try
			{
				return aes.Decrypt(payload);
			}
			catch (CryptographicException e)
			{
				throw new CryptographicException(
					$"[AddressablesEncryption] Cannot decrypt bundle '{bundleId}': wrong key or corrupted file.", e);
			}
		}
	}
}
