using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Encrypts the bundle files of encrypted groups in place after the stock build wrote them.
	/// All-or-nothing: if any bundle cannot be encrypted, every plaintext bundle of an encrypted group is deleted,
	/// so a failed build never leaves unprotected files in the output.
	/// </summary>
	internal static class EncryptedBundleFileEncryptor
	{
		public static void EncryptBundles(AddressablesPlayerBuildResult result, HashSet<AddressableAssetGroup> encryptedGroups, byte[] key)
		{
			var paths = result.AssetBundleBuildResults
				.Where(b => encryptedGroups.Contains(b.SourceAssetGroup) && File.Exists(b.FilePath))
				.Select(b => b.FilePath)
				.Distinct()
				.ToList();
			try
			{
				for (int i = 0; i < paths.Count; i++)
				{
					EditorUtility.DisplayProgressBar("Addressables Encryption", Path.GetFileName(paths[i]), (float)i / paths.Count);

					if (EncryptedBundleFormat.IsEncryptedFile(paths[i]))
						continue; // reused output of a previous build, already encrypted
					File.WriteAllBytes(paths[i], EncryptedBundleFormat.Encrypt(File.ReadAllBytes(paths[i]), key));
				}
			}
			catch
			{
				DeletePlaintextBundles(paths);
				throw;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (paths.Count == 0)
				Debug.LogWarning("[AddressablesEncryption] No bundle was built for the encrypted group(s) "
					+ string.Join(", ", encryptedGroups.Select(g => g.Name))
					+ ". Their entries probably point at assets that are missing from the project (e.g. a sample that is not imported).");

			Debug.Log($"[AddressablesEncryption] Encrypted {paths.Count} bundle(s) from {encryptedGroups.Count} group(s).");
		}

		private static void DeletePlaintextBundles(List<string> paths)
		{
			foreach (var path in paths)
			{
				try
				{
					if (File.Exists(path) && !EncryptedBundleFormat.IsEncryptedFile(path))
						File.Delete(path);
				}
				catch (System.Exception e)
				{
					Debug.LogError($"[AddressablesEncryption] Could not delete plaintext bundle '{path}': {e.Message}");
				}
			}
		}
	}
}
