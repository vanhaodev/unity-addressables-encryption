#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// Streaming audio test content: the Demo sample's song (Music/illit_not_cute_anymore.mp3, local only, not in
	/// the package) set to Load Type Streaming in an encrypted group. Skipped when the file is not in the project.
	/// </summary>
	public partial class PlayModeTestContentBuild
	{
		private const string MusicSearch = "illit_not_cute_anymore t:AudioClip";
		private const string SavedMusicLoadTypeKey = "AddressablesEncryption.Tests.MusicLoadType";

		internal static string FindMusicPath()
		{
			var guids = AssetDatabase.FindAssets(MusicSearch);
			return guids.Length > 0 ? AssetDatabase.GUIDToAssetPath(guids[0]) : null;
		}

		private static void CreateMusicGroup(AddressableAssetSettings settings)
		{
			var path = FindMusicPath();
			if (path == null)
				return;

			var importer = (AudioImporter)AssetImporter.GetAtPath(path);
			var sampleSettings = importer.defaultSampleSettings;
			SessionState.SetInt(SavedMusicLoadTypeKey, (int)sampleSettings.loadType);
			sampleSettings.loadType = AudioClipLoadType.Streaming;
			importer.defaultSampleSettings = sampleSettings;
			importer.SaveAndReimport();

			var group = EnsureEncryptedGroup(settings, MusicGroupName);
			group.GetSchema<BundledAssetGroupSchema>().Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
			AddEntry(settings, group, path, MusicAddress);
		}

		private static void RestoreMusicImporter()
		{
			var saved = SessionState.GetInt(SavedMusicLoadTypeKey, -1);
			SessionState.EraseInt(SavedMusicLoadTypeKey);
			var path = FindMusicPath();
			if (saved < 0 || path == null)
				return;
			var importer = (AudioImporter)AssetImporter.GetAtPath(path);
			var sampleSettings = importer.defaultSampleSettings;
			sampleSettings.loadType = (AudioClipLoadType)saved;
			importer.defaultSampleSettings = sampleSettings;
			importer.SaveAndReimport();
		}
	}
}
#endif
