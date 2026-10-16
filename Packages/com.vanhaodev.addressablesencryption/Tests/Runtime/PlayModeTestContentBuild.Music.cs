#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// Streaming audio test content: the Demo group's "Music" entry (a song the developer adds to the imported Demo
	/// sample; not shipped with the package). Its import is forced to Load Type Streaming for the run and restored
	/// after. The streaming test is ignored when the project has no such entry.
	/// </summary>
	public partial class PlayModeTestContentBuild
	{
		private const string SavedMusicLoadTypeKey = "AddressablesEncryption.Tests.MusicLoadType";

		/// <summary>Asset path of the Demo group's "Music" entry, or null.</summary>
		internal static string FindMusicPath()
		{
			var demo = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings?.FindGroup("Demo");
			if (demo == null)
				return null;
			foreach (var entry in demo.entries)
			{
				if (entry.address != MusicAddress)
					continue;
				var path = AssetDatabase.GUIDToAssetPath(entry.guid);
				return AssetImporter.GetAtPath(path) is AudioImporter ? path : null;
			}
			return null;
		}

		private static void EnsureMusicIsStreaming()
		{
			var path = FindMusicPath();
			if (path == null)
				return;
			var importer = (AudioImporter)AssetImporter.GetAtPath(path);
			var sampleSettings = importer.defaultSampleSettings;
			SessionState.SetInt(SavedMusicLoadTypeKey, (int)sampleSettings.loadType);
			if (sampleSettings.loadType == AudioClipLoadType.Streaming)
				return;
			sampleSettings.loadType = AudioClipLoadType.Streaming;
			importer.defaultSampleSettings = sampleSettings;
			importer.SaveAndReimport();
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
			if (sampleSettings.loadType == (AudioClipLoadType)saved)
				return;
			sampleSettings.loadType = (AudioClipLoadType)saved;
			importer.defaultSampleSettings = sampleSettings;
			importer.SaveAndReimport();
		}
	}
}
#endif
