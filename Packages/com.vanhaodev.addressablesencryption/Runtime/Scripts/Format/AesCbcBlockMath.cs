using System.IO;
using System.Security.Cryptography;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Pure helpers for random access into an AES-CBC encrypted bundle file:<br/>
	/// file = [header 5B][IV 16B][ciphertext, multiple of 16B, PKCS7 padded].<br/>
	/// Plaintext block i = AES_decrypt(C_i) XOR C_(i-1), with C_(-1) = IV, so any block can be decrypted alone
	/// given the previous ciphertext block.
	/// </summary>
	internal static class AesCbcBlockMath
	{
		public const int BlockSize = 16;
		public const int IvSize = 16;
		/// <summary>File offset of the first ciphertext block.</summary>
		public const int CipherStart = EncryptedBundleFormat.HeaderSize + IvSize;

		/// <summary>File offset of ciphertext block <paramref name="blockIndex"/>.</summary>
		public static long CipherOffset(long blockIndex)
		{
			return CipherStart + blockIndex * BlockSize;
		}

		/// <summary>Ciphertext length of a file, or throws when the file cannot be an encrypted bundle.</summary>
		public static long CipherLength(long fileLength, string bundleId)
		{
			var cipherLength = fileLength - CipherStart;
			if (cipherLength < BlockSize)
				throw new InvalidDataException($"[AddressablesEncryption] Bundle '{bundleId}' is truncated ({fileLength} bytes).");
			if (cipherLength % BlockSize != 0)
				throw new InvalidDataException($"[AddressablesEncryption] Bundle '{bundleId}' has an invalid length ({fileLength} bytes): not a multiple of the AES block size.");
			return cipherLength;
		}

		/// <summary>
		/// Number of PKCS7 padding bytes at the end of a decrypted last block (1..16). Any other value means the
		/// key is wrong or the file is damaged: a wrong key gives random bytes here, caught with ~255/256 odds.
		/// </summary>
		public static int PaddingLength(byte[] lastPlainBlock, string bundleId)
		{
			int pad = lastPlainBlock[BlockSize - 1];
			var valid = pad >= 1 && pad <= BlockSize;
			for (int i = BlockSize - pad; valid && i < BlockSize; i++)
				valid = lastPlainBlock[i] == pad;
			if (!valid)
				throw new CryptographicException($"[AddressablesEncryption] Cannot decrypt bundle '{bundleId}': wrong key or corrupted file.");
			return pad;
		}
	}
}
