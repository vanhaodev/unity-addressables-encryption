using System;
using System.Security.Cryptography;
using System.Text;
using vanhaodev.encryptionmanager;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Key for encrypted Addressables bundles.<br/>
	/// Call SetKey once before loading anything from an encrypted group:<br/>
	/// - hardcoded: <c>AddressablesEncryption.SetKey("my-secret");</c><br/>
	/// - from a server: fetch the secret, then call SetKey, then load.<br/>
	/// The key MUST match the build key (Addressables Encryption Settings asset or env var).
	/// </summary>
	public static partial class AddressablesEncryption
	{
		// Fixed, library-specific salt: the same secret always gives the same key,
		// but the key differs from any other use of that secret (e.g. save encryption).
		private static readonly byte[] KeySalt = Encoding.UTF8.GetBytes("vanhaodev.addressablesencryption/keys/v1");
		private const int KeyIterations = 10000;
		private const int KeySize = 32;

		// Read from background decrypt threads; swapped as a whole reference, never mutated.
		private static volatile AesEncryption _aes;

		/// <summary>True once SetKey was called.</summary>
		public static bool HasKey => _aes != null;

		/// <summary>
		/// Set the key from a secret string of any length. It is stretched (PBKDF2-SHA256) into a 256-bit AES key.
		/// </summary>
		public static void SetKey(string secret)
		{
			SetKey(DeriveKey(secret));
		}

		/// <summary>Set a raw AES key (16, 24 or 32 bytes).</summary>
		public static void SetKey(byte[] key)
		{
			ValidateRawKey(key);
			_aes = new AesEncryption((byte[])key.Clone());
		}

		/// <summary>Forget the key. Encrypted bundles fail to load until SetKey is called again.</summary>
		public static void ClearKey()
		{
			_aes = null;
		}

		/// <summary>Stretch a secret string into the 256-bit AES key used by <see cref="SetKey(string)"/>.</summary>
		public static byte[] DeriveKey(string secret)
		{
			if (string.IsNullOrEmpty(secret))
				throw new ArgumentException("Secret must not be empty.", nameof(secret));

			using var pbkdf2 = new Rfc2898DeriveBytes(secret, KeySalt, KeyIterations, HashAlgorithmName.SHA256);
			return pbkdf2.GetBytes(KeySize);
		}

		/// <summary>Throws if the key is not a valid AES key size.</summary>
		public static void ValidateRawKey(byte[] key)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (key.Length != 16 && key.Length != 24 && key.Length != 32)
				throw new ArgumentException($"AES key must be 16, 24 or 32 bytes, got {key.Length}.", nameof(key));
		}

		/// <summary>Decrypt an encrypted bundle file with the current key. Thread-safe.</summary>
		internal static byte[] DecryptBundle(byte[] fileBytes, string bundleId)
		{
			var aes = _aes;
			if (aes == null)
				throw new InvalidOperationException(
					$"[AddressablesEncryption] No key set while loading encrypted bundle '{bundleId}'. Call AddressablesEncryption.SetKey before loading.");

			return EncryptedBundleFormat.Decrypt(fileBytes, aes, bundleId);
		}
	}
}
