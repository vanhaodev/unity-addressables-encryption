using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// One-click setup: create the key settings asset, register the encrypted build script
	/// and make it the active player builder. Safe to run again.
	/// </summary>
	internal static class AddressablesEncryptionSetupMenu
	{
		[MenuItem("Tools/Addressables Encryption/Setup")]
		private static void Setup()
		{
			if (!AddressableAssetSettingsDefaultObject.SettingsExists)
			{
				EditorUtility.DisplayDialog("Addressables Encryption",
					"Addressables is not set up yet. Open Window > Asset Management > Addressables > Groups and click 'Create Addressables Settings' first.", "OK");
				return;
			}

			var settings = AddressableAssetSettingsDefaultObject.Settings;
			var keySettings = EnsureKeySettings(settings);
			EnsureActiveBuilder(settings);

			Selection.activeObject = keySettings;
			EditorGUIUtility.PingObject(keySettings);
			Debug.Log("[AddressablesEncryption] Setup done. Set the build key in the selected asset, then add the 'Encryption' schema to groups to encrypt.");
		}

		private static AddressablesEncryptionSettings EnsureKeySettings(AddressableAssetSettings settings)
		{
			var existing = AddressablesEncryptionSettings.Find();
			if (existing != null)
				return existing;

			var path = Path.Combine(settings.ConfigFolder, nameof(AddressablesEncryptionSettings) + ".asset").Replace('\\', '/');
			var asset = ScriptableObject.CreateInstance<AddressablesEncryptionSettings>();
			AssetDatabase.CreateAsset(asset, path);
			AssetDatabase.SaveAssets();
			return asset;
		}

		private static void EnsureActiveBuilder(AddressableAssetSettings settings)
		{
			var index = settings.DataBuilders.FindIndex(b => b is BuildScriptPackedEncryptedMode);
			if (index < 0)
			{
				if (!Directory.Exists(settings.DataBuilderFolder))
					Directory.CreateDirectory(settings.DataBuilderFolder);

				var path = Path.Combine(settings.DataBuilderFolder, nameof(BuildScriptPackedEncryptedMode) + ".asset").Replace('\\', '/');
				var builder = AssetDatabase.LoadAssetAtPath<BuildScriptPackedEncryptedMode>(path);
				if (builder == null)
				{
					builder = ScriptableObject.CreateInstance<BuildScriptPackedEncryptedMode>();
					AssetDatabase.CreateAsset(builder, path);
				}

				settings.AddDataBuilder(builder);
				index = settings.DataBuilders.Count - 1;
			}

			settings.ActivePlayerDataBuilderIndex = index;
			AssetDatabase.SaveAssets();
		}
	}
}
