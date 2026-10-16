using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;
using vanhaodev.addressablesencryption.Editor;

namespace vanhaodev.addressablesencryption.Tests
{
	/// <summary>
	/// "Encrypt Catalog": the built catalog.bin carries the AAEN header, settings.json points at the encrypted
	/// catalog provider and registers the initialization object, and the file decrypts with the build key.
	/// (Loading through Addressables is covered by the PlayMode suite, which builds with the flag on.)
	/// </summary>
	public class EncryptedCatalogBuildTests
	{
		private AddressableAssetSettings _settings;
		private AddressablesEncryptionSettings _keySettings;
		private bool _savedEncryptCatalog;
		private int _savedBuilderIndex;

		[SetUp]
		public void SetUp()
		{
			_settings = AddressableAssetSettingsDefaultObject.Settings;
			Assert.IsNotNull(_settings, "Addressables settings not found.");
			LogAssert.ignoreFailingMessages = true;
			_savedBuilderIndex = _settings.ActivePlayerDataBuilderIndex;
			_keySettings = AddressablesEncryptionSetupMenu.Setup(_settings);
			_savedEncryptCatalog = _keySettings.EncryptCatalog;
			_keySettings.EncryptCatalog = true;
		}

		[TearDown]
		public void TearDown()
		{
			LogAssert.ignoreFailingMessages = false;
			_keySettings.EncryptCatalog = _savedEncryptCatalog;
			_settings.ActivePlayerDataBuilderIndex = _savedBuilderIndex;
			AssetDatabase.SaveAssets();
		}

		[Test]
		public void Build_EncryptsLocalCatalog_AndWiresRuntime()
		{
			AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
			Assert.IsTrue(string.IsNullOrEmpty(result.Error), "build failed: " + result.Error);

			var catalogPath = Path.Combine(Addressables.BuildPath, "catalog.bin");
			Assert.IsTrue(File.Exists(catalogPath), "catalog.bin exists");
			Assert.IsTrue(EncryptedBundleFormat.IsEncryptedFile(catalogPath), "catalog.bin starts with AAEN");

			var settingsJson = File.ReadAllText(Path.Combine(Addressables.BuildPath, EncryptedCatalogPostProcessor.SettingsFileName));
			StringAssert.Contains(typeof(EncryptedBinaryCatalogProvider).FullName, settingsJson, "catalog location uses the encrypted provider");
			StringAssert.Contains(typeof(EncryptedCatalogInitialization).FullName, settingsJson, "initialization object registered");

			var key = new vanhaodev.encryptionmanager.AesEncryption(AddressablesEncryptionSettings.ResolveBuildKey());
			var plain = EncryptedBundleFormat.Decrypt(File.ReadAllBytes(catalogPath), key, "catalog");
			Assert.Greater(plain.Length, 0, "catalog decrypts with the build key");
			Assert.IsFalse(EncryptedBundleFormat.IsEncrypted(plain), "decrypted catalog is the plain binary catalog");
		}
	}
}
