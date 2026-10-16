using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using vanhaodev.addressablesencryption;

namespace Demo.Tests
{
	/// <summary>
	/// Remote encrypted bundle + disk cache, against a local HTTP server.
	/// Requires Tools > Addressables Encryption > Demo > Build And Verify first.
	/// </summary>
	public class EncryptedRemoteCachePlayModeTests
	{
		private const string Address = "RemoteCapsule";
		private static string CacheRoot => Path.Combine(Application.persistentDataPath, "AddressablesEncryption", "Cache");

		private LocalBundleServer _server;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			_server = new LocalBundleServer();
			AddressablesEncryption.ClearCache();
			AddressablesEncryption.SetKey("demo-secret");
			yield return Addressables.InitializeAsync();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			_server.Dispose();
			AddressablesEncryption.ClearCache();
			AddressablesEncryption.ClearKey();
			yield return null;
		}

		[UnityTest]
		public IEnumerator FirstLoadDownloads_SecondLoadUsesCache()
		{
			yield return LoadAndRelease();
			Assert.AreEqual(1, _server.RequestCount, "first load downloads once");

			yield return LoadAndRelease();
			Assert.AreEqual(1, _server.RequestCount, "second load is served from the encrypted cache");
		}

		[UnityTest]
		public IEnumerator DownloadDependencies_FillsCache_DownloadSizeBecomesZero()
		{
			var before = AddressablesEncryption.GetDownloadSizeAsync(Address);
			yield return Wait(before);
			Assert.Greater(before.Result, 0, "not cached yet");

			var download = Addressables.DownloadDependenciesAsync(Address);
			yield return download;
			Assert.AreEqual(AsyncOperationStatus.Succeeded, download.Status, download.OperationException?.ToString());
			download.Release();
			Assert.AreEqual(1, _server.RequestCount);

			var after = AddressablesEncryption.GetDownloadSizeAsync(Address);
			yield return Wait(after);
			Assert.AreEqual(0, after.Result, "cached");

			yield return LoadAndRelease();
			Assert.AreEqual(1, _server.RequestCount, "load after pre-download uses the cache");
		}

		[UnityTest]
		public IEnumerator CorruptedCacheFile_IsDownloadedAgain()
		{
			yield return LoadAndRelease();
			var cached = Directory.GetFiles(CacheRoot, "*.aaen", SearchOption.AllDirectories);
			Assert.AreEqual(1, cached.Length);

			var bytes = File.ReadAllBytes(cached[0]);
			for (int i = bytes.Length / 2; i < bytes.Length; i++)
				bytes[i] ^= 0x5A; // break the ciphertext, keep the header
			File.WriteAllBytes(cached[0], bytes);

			yield return LoadAndRelease();
			Assert.AreEqual(2, _server.RequestCount, "corrupted cache triggers one new download");
		}

		[UnityTest]
		public IEnumerator CacheFiles_StayEncrypted_AndHideNames()
		{
			yield return LoadAndRelease();
			var files = Directory.GetFiles(CacheRoot, "*", SearchOption.AllDirectories);
			Assert.IsNotEmpty(files, "remote bundle was cached");
			foreach (var file in files)
			{
				StringAssert.DoesNotContain("demo", file.ToLowerInvariant());
				StringAssert.DoesNotContain("remote", Path.GetFileName(file).ToLowerInvariant());
				Assert.IsTrue(EncryptedBundleFormat.IsEncrypted(File.ReadAllBytes(file)), file);
			}
		}

		private static IEnumerator LoadAndRelease()
		{
			var handle = Addressables.LoadAssetAsync<GameObject>(Address);
			yield return handle;
			Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, handle.OperationException?.ToString());
			handle.Release();
			yield return null; // let the bundle unload
		}

		private static IEnumerator Wait(Task task)
		{
			yield return new WaitUntil(() => task.IsCompleted);
			if (task.IsFaulted)
				throw task.Exception.GetBaseException();
		}
	}
}
