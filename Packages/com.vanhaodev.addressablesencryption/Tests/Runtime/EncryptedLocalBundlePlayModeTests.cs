using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// End-to-end load of the local encrypted "Demo" group (address "Hanni", a Texture2D).
	/// <see cref="PlayModeTestContentBuild"/> builds the content before play mode starts.
	/// </summary>
	[PrebuildSetup(typeof(PlayModeTestContentBuild))]
	[PostBuildCleanup(typeof(PlayModeTestContentBuild))]
	public class EncryptedLocalBundlePlayModeTests
	{
		private const string Address = PlayModeTestContentBuild.LocalAddress;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			// The catalog is encrypted too, so the key must be there before Addressables initializes.
			AddressablesEncryption.SetKey(PlayModeTestContentBuild.Secret);
			yield return Addressables.InitializeAsync();
			AddressablesEncryption.ClearKey();
		}

		[Test]
		public void Catalog_OnDisk_IsEncrypted()
		{
			var catalogPath = System.IO.Path.Combine(Addressables.RuntimePath, "catalog.bin");
			Assert.IsTrue(System.IO.File.Exists(catalogPath), catalogPath);
			Assert.IsTrue(EncryptedBundleFormat.IsEncryptedFile(catalogPath), "catalog.bin starts with AAEN");
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			AddressablesEncryption.ClearKey();
			yield return null;
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_LoadAsync()
		{
			AddressablesEncryption.SetKey(PlayModeTestContentBuild.Secret);

			var handle = Addressables.LoadAssetAsync<Texture2D>(Address);
			yield return handle;

			Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, handle.OperationException?.ToString());
			Assert.IsNotNull(handle.Result);
			Assert.Greater(handle.Result.width, 0);
			handle.Release();
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_LoadSync_WaitForCompletion()
		{
			AddressablesEncryption.SetKey(PlayModeTestContentBuild.Secret);

			var handle = Addressables.LoadAssetAsync<Texture2D>(Address);
			var result = handle.WaitForCompletion();

			Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, handle.OperationException?.ToString());
			Assert.IsNotNull(result);
			handle.Release();
			yield return null;
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_Reload_AfterRelease()
		{
			AddressablesEncryption.SetKey(PlayModeTestContentBuild.Secret);

			for (int i = 0; i < 2; i++)
			{
				var handle = Addressables.LoadAssetAsync<Texture2D>(Address);
				yield return handle;
				Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, handle.OperationException?.ToString());
				handle.Release();
				yield return null; // let the bundle unload before loading it again
			}
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_WrongKey_Fails()
		{
			AddressablesEncryption.SetKey("wrong-secret");
			LogAssert.ignoreFailingMessages = true; // Addressables logs the provider exception as an error

			var handle = Addressables.LoadAssetAsync<Texture2D>(Address);
			yield return handle;

			Assert.AreEqual(AsyncOperationStatus.Failed, handle.Status);
			handle.Release();
			LogAssert.ignoreFailingMessages = false;
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_NoKey_Fails()
		{
			LogAssert.ignoreFailingMessages = true;

			var handle = Addressables.LoadAssetAsync<Texture2D>(Address);
			yield return handle;

			Assert.AreEqual(AsyncOperationStatus.Failed, handle.Status);
			StringAssert.Contains("No key set", handle.OperationException?.ToString());
			handle.Release();
			LogAssert.ignoreFailingMessages = false;
		}
	}
}
