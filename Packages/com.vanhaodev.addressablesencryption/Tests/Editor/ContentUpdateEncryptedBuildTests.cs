using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.TestTools;
using vanhaodev.addressablesencryption.Editor;

namespace vanhaodev.addressablesencryption.Tests
{
	/// <summary>
	/// "Update a Previous Build" with an encrypted 'Prevent Updates' group: the changed asset lands in a new group
	/// that inherits Encryption, the unchanged asset keeps its previous encrypted bundle, and flipping the setting
	/// on the static group is refused. Builds real content twice, so this takes a while.
	/// </summary>
	public class ContentUpdateEncryptedBuildTests
	{
		private const string Root = "Assets/AddressablesEncryptionTestContent";
		private const string Folder = Root + "/ContentUpdate";
		private const string GroupName = "AE CU Test";
		private const string UpdateGroupName = "AE CU Update";
		private const string PathA = Folder + "/A.png";
		private const string PathB = Folder + "/B.png";

		private AddressableAssetSettings _settings;
		private AddressableAssetGroup _group;
		private string _statePath;
		private string _stateBackup;
		private int _savedBuilderIndex;
		private bool _savedBuildRemoteCatalog;
		private string _savedRemoteCatalogBuildPathId;
		private string _savedRemoteCatalogLoadPathId;

		[SetUp]
		public void SetUp()
		{
			_settings = AddressableAssetSettingsDefaultObject.Settings;
			Assert.IsNotNull(_settings, "Addressables settings not found.");
			LogAssert.ignoreFailingMessages = true; // Addressables logs warnings/errors of its own during content updates

			_savedBuilderIndex = _settings.ActivePlayerDataBuilderIndex;
			AddressablesEncryptionSetupMenu.Setup(_settings);

			// Addressables refuses content updates for builds without a remote catalog.
			_savedBuildRemoteCatalog = _settings.BuildRemoteCatalog;
			_savedRemoteCatalogBuildPathId = _settings.RemoteCatalogBuildPath.Id;
			_savedRemoteCatalogLoadPathId = _settings.RemoteCatalogLoadPath.Id;
			_settings.BuildRemoteCatalog = true;
			_settings.RemoteCatalogBuildPath.SetVariableByName(_settings, AddressableAssetSettings.kRemoteBuildPath);
			_settings.RemoteCatalogLoadPath.SetVariableByName(_settings, AddressableAssetSettings.kRemoteLoadPath);

			_statePath = ContentUpdateScript.GetContentStateDataPath(false, _settings);
			_stateBackup = File.Exists(_statePath) ? Path.GetTempFileName() : null;
			if (_stateBackup != null)
				File.Copy(_statePath, _stateBackup, true);

			Directory.CreateDirectory(Folder);
			WritePng(PathA, 1);
			WritePng(PathB, 2);

			_group = _settings.CreateGroup(GroupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
			_group.AddSchema<EncryptedBundleGroupSchema>();
			_group.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
			_group.GetSchema<ContentUpdateGroupSchema>().StaticContent = true;
			AddEntry(PathA, "CU_A");
			AddEntry(PathB, "CU_B");
			AssetDatabase.SaveAssets();
		}

		[TearDown]
		public void TearDown()
		{
			LogAssert.ignoreFailingMessages = false;
			foreach (var group in _settings.groups.Where(g => g != null && (g.Name == GroupName || g.Name.StartsWith(UpdateGroupName))).ToList())
				_settings.RemoveGroup(group);

			if (_stateBackup != null)
			{
				File.Copy(_stateBackup, _statePath, true);
				File.Delete(_stateBackup);
			}
			else if (File.Exists(_statePath))
			{
				File.Delete(_statePath);
			}

			_settings.ActivePlayerDataBuilderIndex = _savedBuilderIndex;
			_settings.BuildRemoteCatalog = _savedBuildRemoteCatalog;
			// ProfileValueReference cannot be set back to "undefined" through its API; restore the serialized ids.
			var so = new SerializedObject(_settings);
			so.FindProperty("m_RemoteCatalogBuildPath.m_Id").stringValue = _savedRemoteCatalogBuildPathId;
			so.FindProperty("m_RemoteCatalogLoadPath.m_Id").stringValue = _savedRemoteCatalogLoadPathId;
			so.ApplyModifiedPropertiesWithoutUndo();
			AssetDatabase.DeleteAsset(Folder);
			if (Directory.Exists(Root) && Directory.GetFileSystemEntries(Root).Length == 0)
				AssetDatabase.DeleteAsset(Root);
			AssetDatabase.SaveAssets();
		}

		[Test]
		public void ContentUpdate_NewGroupInheritsEncryption_PreviousBundlesStayEncrypted()
		{
			var previousBundles = FullBuild();
			Assert.AreEqual(2, previousBundles.Count, "pack separately: one bundle per asset");
			foreach (var path in previousBundles)
				Assert.IsTrue(EncryptedBundleFormat.IsEncryptedFile(path), path);

			WritePng(PathA, 3); // A changes, B stays
			var modified = ContentUpdateScript.GatherModifiedEntriesWithDependencies(_settings, _statePath);
			Assert.IsTrue(modified.Keys.Any(e => e.address == "CU_A"), "A is detected as modified");
			ContentUpdateScript.CreateContentUpdateGroup(_settings, modified.Keys.ToList(), UpdateGroupName);

			var update = ContentUpdateScript.BuildContentUpdate(_settings, _statePath);
			Assert.IsTrue(string.IsNullOrEmpty(update.Error), "content update failed: " + update.Error);

			var updateGroup = _settings.groups.First(g => g != null && g.Name.StartsWith(UpdateGroupName));
			Assert.IsTrue(EncryptedBundleGroupSchema.IsEncrypted(updateGroup), "update group inherited the Encryption schema");

			var updateBundles = BundlesOf(update, updateGroup);
			Assert.AreEqual(1, updateBundles.Count);
			Assert.IsTrue(EncryptedBundleFormat.IsEncryptedFile(updateBundles[0]), "new bundle is encrypted");

			foreach (var path in previousBundles)
				Assert.IsTrue(File.Exists(path) && EncryptedBundleFormat.IsEncryptedFile(path), "previous bundle untouched: " + path);
			var names = previousBundles.SelectMany(AssetNamesIn).ToList();
			Assert.IsTrue(names.Any(n => n.EndsWith("B.png", System.StringComparison.OrdinalIgnoreCase)),
				"B's previous encrypted bundle still loads with the build key; assets found: " + string.Join(", ", names));
		}

		[Test]
		public void ContentUpdate_EncryptionRemovedFromStaticGroup_IsRefused()
		{
			FullBuild();
			_group.GetSchema<EncryptedBundleGroupSchema>().IsEnabled = false;

			// Addressables logs the build exception once as an exception and once as an error.
			var expected = new System.Text.RegularExpressions.Regex("Encryption setting");
			LogAssert.Expect(LogType.Exception, expected);
			LogAssert.Expect(LogType.Error, expected);
			var update = ContentUpdateScript.BuildContentUpdate(_settings, _statePath);
			StringAssert.Contains("Encryption setting", update.Error ?? "");
		}

		private List<string> FullBuild()
		{
			AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
			Assert.IsTrue(string.IsNullOrEmpty(result.Error), "full build failed: " + result.Error);
			Assert.IsTrue(File.Exists(_statePath), "content state written");
			return BundlesOf(result, _group);
		}

		private static List<string> BundlesOf(AddressablesPlayerBuildResult result, AddressableAssetGroup group)
		{
			return result.AssetBundleBuildResults.Where(b => b.SourceAssetGroup == group).Select(b => b.FilePath).Distinct().ToList();
		}

		/// <summary>Decrypt a built bundle with the build key and list its assets (how an old player would load it).</summary>
		private static string[] AssetNamesIn(string path)
		{
			var plain = EncryptedBundleFormat.Decrypt(File.ReadAllBytes(path), new vanhaodev.encryptionmanager.AesEncryption(AddressablesEncryptionSettings.ResolveBuildKey()), path);
			var bundle = AssetBundle.LoadFromMemory(plain);
			Assert.IsNotNull(bundle, "decrypted bundle loads: " + path);
			var names = bundle.GetAllAssetNames();
			bundle.Unload(true);
			return names;
		}

		private void AddEntry(string assetPath, string address)
		{
			var entry = _settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(assetPath), _group);
			entry.address = address;
		}

		private static void WritePng(string path, int seed)
		{
			var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
			var pixels = new Color32[64 * 64];
			var random = new System.Random(seed);
			for (int i = 0; i < pixels.Length; i++)
				pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
			texture.SetPixels32(pixels);
			texture.Apply();
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
		}
	}
}
