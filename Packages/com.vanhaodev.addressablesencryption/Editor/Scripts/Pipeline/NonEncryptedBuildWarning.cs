using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEngine;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Groups with the Encryption schema built by any other data builder ship as plaintext and loading still works,
	/// so the leak would go unnoticed. Warn after such a build.
	/// </summary>
	[InitializeOnLoad]
	internal static class NonEncryptedBuildWarning
	{
		static NonEncryptedBuildWarning()
		{
			BuildScript.buildCompleted -= OnBuildCompleted;
			BuildScript.buildCompleted += OnBuildCompleted;
		}

		private static void OnBuildCompleted(AddressableAssetBuildResult result)
		{
			if (!(result is AddressablesPlayerBuildResult) || !string.IsNullOrEmpty(result.Error))
				return;

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null || settings.ActivePlayerDataBuilder is BuildScriptPackedEncryptedMode)
				return;

			var groups = settings.groups.Where(EncryptedBundleGroupSchema.IsEncrypted).Select(g => g.Name).ToList();
			if (groups.Count > 0)
				Debug.LogWarning(
					$"[AddressablesEncryption] Addressables was built with '{settings.ActivePlayerDataBuilder?.Name}', so these groups are NOT encrypted: {string.Join(", ", groups)}. " +
					"Use Tools > Addressables Encryption > Setup to select the encrypted build script, then rebuild.");
		}
	}
}
