using UnityEngine.TestTools;
#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using vanhaodev.addressablesencryption.Editor;
#endif

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// Runs in the Editor before the PlayMode tests enter play mode: builds Addressables with the encrypted build
	/// script so the tests load real encrypted bundles. Uses the project's "Demo" group (local, needs the Demo sample
	/// imported) plus two temporary groups: a remote one served by <see cref="LocalBundleServer"/> and a large local
	/// one for the memory test. Cleanup restores the project. Group creation lives in the Groups partial.
	/// </summary>
	public partial class PlayModeTestContentBuild : IPrebuildSetup, IPostBuildCleanup
	{
		public const string Secret = "demo-secret";
		public const string LocalAddress = "Hanni";
		public const string RemoteAddress = "TestRemoteTexture";
		public const string LargeAddress = "TestLargeTexture";
		public const string MusicAddress = "Music"; // entry of the Demo group (local song, see .Music partial)
		public const int LargeTextureSize = 2048; // 2048² RGBA32 = 16 MB uncompressed
		public const string RemoteLoadPath = "http://localhost:" + LocalBundleServer.PortText + "/[BuildTarget]";

		private const string RemoteGroupName = "AE Test Remote";
		private const string LargeGroupName = "AE Test Large";
		private const string ContentFolder = "Assets/AddressablesEncryptionTestContent";
		private const string SavedLoadPathKey = "AddressablesEncryption.Tests.RemoteLoadPath";
		private const string SavedPlayModeBuilderKey = "AddressablesEncryption.Tests.PlayModeBuilder";
		private const string SavedDemoEncryptionKey = "AddressablesEncryption.Tests.DemoEncryption";
		private const string SavedEncryptCatalogKey = "AddressablesEncryption.Tests.EncryptCatalog";
		private const string NotSaved = "\0"; // SessionState returns "" for a missing string, so use a sentinel

#if UNITY_EDITOR
		public void Setup()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			Assert.IsNotNull(settings, "Addressables settings not found. Create them first.");

			var keySettings = AddressablesEncryptionSetupMenu.Setup(settings);
			Assert.AreEqual(Secret, keySettings.Key, $"AddressablesEncryptionSettings key must be '{Secret}' for the tests.");
			// The whole PlayMode suite runs against an encrypted catalog (fixtures set the key before initializing).
			SessionState.SetInt(SavedEncryptCatalogKey, keySettings.EncryptCatalog ? 1 : 0);
			keySettings.EncryptCatalog = true;

			RequireDemoGroup(settings);
			Directory.CreateDirectory(ContentFolder);
			CreateRemoteGroup(settings);
			CreateLargeGroup(settings);
			EnsureMusicIsStreaming();
			UsePlayModeExistingBuild(settings);
			AssetDatabase.SaveAssets();

			AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
			Assert.IsTrue(string.IsNullOrEmpty(result.Error), "Addressables build failed: " + result.Error);
		}

		public void Cleanup()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
				return;

			RemoveGroup(settings, RemoteGroupName);
			RemoveGroup(settings, LargeGroupName);
			RestoreMusicImporter();

			var savedLoadPath = SessionState.GetString(SavedLoadPathKey, NotSaved);
			if (savedLoadPath != NotSaved)
				settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, savedLoadPath);

			var savedBuilder = SessionState.GetInt(SavedPlayModeBuilderKey, -1);
			if (savedBuilder >= 0)
				settings.ActivePlayModeDataBuilderIndex = savedBuilder;

			var savedDemoEncryption = SessionState.GetInt(SavedDemoEncryptionKey, -1);
			var demoSchema = settings.FindGroup("Demo")?.GetSchema<EncryptedBundleGroupSchema>();
			if (savedDemoEncryption >= 0 && demoSchema != null)
				demoSchema.IsEnabled = savedDemoEncryption == 1;

			var savedEncryptCatalog = SessionState.GetInt(SavedEncryptCatalogKey, -1);
			var keySettings = AddressablesEncryptionSettings.Find();
			if (savedEncryptCatalog >= 0 && keySettings != null)
				keySettings.EncryptCatalog = savedEncryptCatalog == 1;

			SessionState.EraseString(SavedLoadPathKey);
			SessionState.EraseInt(SavedPlayModeBuilderKey);
			SessionState.EraseInt(SavedDemoEncryptionKey);
			SessionState.EraseInt(SavedEncryptCatalogKey);
			AssetDatabase.DeleteAsset(ContentFolder);
			AssetDatabase.SaveAssets();
		}

		internal static void RemoveGroup(AddressableAssetSettings settings, string name)
		{
			var group = settings.FindGroup(name);
			if (group != null)
				settings.RemoveGroup(group);
		}

		/// <summary>
		/// The Demo group's asset lives in the gitignored sample import, so check it is really there. Its Encryption
		/// schema is switched on for the test run (the developer may have it off) and restored by Cleanup.
		/// </summary>
		private static void RequireDemoGroup(AddressableAssetSettings settings)
		{
			var demo = settings.FindGroup("Demo");
			Assert.IsNotNull(demo, "Group 'Demo' not found.");

			var schema = demo.GetSchema<EncryptedBundleGroupSchema>() ?? demo.AddSchema<EncryptedBundleGroupSchema>();
			SessionState.SetInt(SavedDemoEncryptionKey, schema.IsEnabled ? 1 : 0);
			schema.IsEnabled = true;

			var found = false;
			foreach (var entry in demo.entries)
			{
				if (entry.address != LocalAddress)
					continue;
				var path = AssetDatabase.GUIDToAssetPath(entry.guid);
				found = !string.IsNullOrEmpty(path) && File.Exists(path);
			}
			Assert.IsTrue(found, $"Entry '{LocalAddress}' of group 'Demo' is missing: import the Demo sample first (Package Manager > Addressables Encryption > Samples).");
		}
#else
		public void Setup() { }
		public void Cleanup() { }
#endif
	}
}
