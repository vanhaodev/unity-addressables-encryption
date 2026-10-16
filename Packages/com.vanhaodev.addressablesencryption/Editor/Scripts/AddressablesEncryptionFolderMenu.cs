using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.AddressableAssets;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Shortcuts to the folders worth inspecting: built bundles (check they start with "AAEN") and the
	/// encrypted download cache used in Play Mode.
	/// </summary>
	internal static class AddressablesEncryptionFolderMenu
	{
		private const string Menu = "Tools/Addressables Encryption/Open Folder/";

		[MenuItem(Menu + "Local Build (Addressables)")]
		private static void OpenLocalBuild()
		{
			Open(Path.GetFullPath(Addressables.BuildPath), "Build Addressables first.");
		}

		[MenuItem(Menu + "Remote Build (ServerData)")]
		private static void OpenRemoteBuild()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				EditorUtility.DisplayDialog("Addressables Encryption", "Addressables is not set up in this project.", "OK");
				return;
			}

			var profiles = settings.profileSettings;
			var raw = profiles.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteBuildPath);
			Open(Path.GetFullPath(profiles.EvaluateString(settings.activeProfileId, raw)), "Build a group whose Build Path is Remote first.");
		}

		[MenuItem(Menu + "Encrypted Bundle Cache")]
		private static void OpenCache()
		{
			Open(EncryptedBundleCache.Root, "Load a remote encrypted bundle (Play Mode) first.");
		}

		private static void Open(string folder, string hintWhenMissing)
		{
			if (!Directory.Exists(folder))
			{
				EditorUtility.DisplayDialog("Addressables Encryption", $"Folder does not exist yet:\n{folder}\n\n{hintWhenMissing}", "OK");
				return;
			}
			EditorUtility.OpenWithDefaultApp(folder);
		}
	}
}
