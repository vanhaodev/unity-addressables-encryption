using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using vanhaodev.addressablesencryption;

namespace vanhaodev.addressablesencryption.Samples.Tests
{
	/// <summary>
	/// End-to-end load of the demo groups. Requires Tools > Addressables Encryption > Demo > Build And Verify first
	/// (it builds with key "demo-secret" and sets Play Mode Script = Use Existing Build).
	/// </summary>
	public class EncryptedDemoPlayModeTests
	{
		[UnitySetUp]
		public IEnumerator SetUp()
		{
			AddressablesEncryption.ClearKey();
			yield return Addressables.InitializeAsync();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			AddressablesEncryption.ClearKey();
			yield return null;
		}

		[UnityTest]
		public IEnumerator EncryptedAndPlainAssets_LoadAsync()
		{
			AddressablesEncryption.SetKey("demo-secret");

			var encrypted = Addressables.LoadAssetAsync<GameObject>("EncryptedCube");
			var plain = Addressables.LoadAssetAsync<GameObject>("PlainSphere");
			yield return encrypted;
			yield return plain;

			Assert.AreEqual(AsyncOperationStatus.Succeeded, encrypted.Status, encrypted.OperationException?.ToString());
			Assert.AreEqual(AsyncOperationStatus.Succeeded, plain.Status, plain.OperationException?.ToString());
			Assert.AreEqual("EncryptedCube", encrypted.Result.name);

			encrypted.Release();
			plain.Release();
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_LoadSync_WaitForCompletion()
		{
			AddressablesEncryption.SetKey("demo-secret");

			var handle = Addressables.LoadAssetAsync<GameObject>("EncryptedCube");
			var result = handle.WaitForCompletion();

			Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, handle.OperationException?.ToString());
			Assert.IsNotNull(result);
			handle.Release();
			yield return null;
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_Reload_AfterRelease()
		{
			AddressablesEncryption.SetKey("demo-secret");

			for (int i = 0; i < 2; i++)
			{
				var handle = Addressables.LoadAssetAsync<GameObject>("EncryptedCube");
				yield return handle;
				Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, handle.OperationException?.ToString());
				handle.Release();
				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_WrongKey_Fails()
		{
			AddressablesEncryption.SetKey("wrong-secret");
			LogAssert.ignoreFailingMessages = true; // Addressables logs the provider exception as an error

			var handle = Addressables.LoadAssetAsync<GameObject>("EncryptedCube");
			yield return handle;

			Assert.AreEqual(AsyncOperationStatus.Failed, handle.Status);
			handle.Release();
			LogAssert.ignoreFailingMessages = false;
		}

		[UnityTest]
		public IEnumerator EncryptedAsset_NoKey_Fails()
		{
			LogAssert.ignoreFailingMessages = true;

			var handle = Addressables.LoadAssetAsync<GameObject>("EncryptedCube");
			yield return handle;

			Assert.AreEqual(AsyncOperationStatus.Failed, handle.Status);
			StringAssert.Contains("No key set", handle.OperationException?.ToString());
			handle.Release();
			LogAssert.ignoreFailingMessages = false;
		}
	}
}
