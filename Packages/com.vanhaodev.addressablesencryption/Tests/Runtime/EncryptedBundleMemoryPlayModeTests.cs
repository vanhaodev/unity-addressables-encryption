using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Profiling;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// Stream decryption must not pull the bundle into managed memory: loading a 16 MB encrypted bundle may grow
	/// the Mono heap by the stream buffers plus Addressables' own garbage (measured ~4 MB without a GC), never by
	/// the bundle size (decrypt-to-memory needed 32+ MB here). The limit is half the bundle.
	/// </summary>
	[PrebuildSetup(typeof(PlayModeTestContentBuild))]
	[PostBuildCleanup(typeof(PlayModeTestContentBuild))]
	public class EncryptedBundleMemoryPlayModeTests
	{
		private const long MaxManagedGrowth = 8L * 1024 * 1024;

		[UnityTest]
		public IEnumerator LargeBundle_LoadsWithSmallManagedPeak()
		{
			AddressablesEncryption.SetKey(PlayModeTestContentBuild.Secret);
			yield return Addressables.InitializeAsync();

			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			var before = Profiler.GetMonoUsedSizeLong();
			var peak = before;

			var handle = Addressables.LoadAssetAsync<Texture2D>(PlayModeTestContentBuild.LargeAddress);
			while (!handle.IsDone)
			{
				peak = Math.Max(peak, Profiler.GetMonoUsedSizeLong());
				yield return null;
			}
			peak = Math.Max(peak, Profiler.GetMonoUsedSizeLong());

			Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, handle.OperationException?.ToString());
			Assert.AreEqual(PlayModeTestContentBuild.LargeTextureSize, handle.Result.width);

			var growth = peak - before;
			Debug.Log($"[AddressablesEncryption tests] managed heap growth while loading the large bundle: {growth / 1024} KB");
			Assert.Less(growth, MaxManagedGrowth, "bundle was decrypted into managed memory");

			handle.Release();
			AddressablesEncryption.ClearKey();
		}
	}
}
