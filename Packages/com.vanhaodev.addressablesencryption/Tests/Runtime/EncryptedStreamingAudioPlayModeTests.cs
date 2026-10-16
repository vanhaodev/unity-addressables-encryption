using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// A Load Type "Streaming" AudioClip inside an encrypted bundle: with stream decryption Unity reads the audio
	/// through the decrypting stream while it plays. Uses the Demo group's "Music" entry; ignored when the project has none.
	/// </summary>
	[PrebuildSetup(typeof(PlayModeTestContentBuild))]
	[PostBuildCleanup(typeof(PlayModeTestContentBuild))]
	public class EncryptedStreamingAudioPlayModeTests
	{
		[UnityTest]
		public IEnumerator StreamingClip_FromEncryptedBundle_Plays()
		{
			AddressablesEncryption.SetKey(PlayModeTestContentBuild.Secret);
			yield return Addressables.InitializeAsync();

			var locate = Addressables.LoadResourceLocationsAsync(PlayModeTestContentBuild.MusicAddress);
			yield return locate;
			var present = locate.Result != null && locate.Result.Count > 0;
			locate.Release();
			if (!present)
				Assert.Ignore("The Demo group has no \"Music\" entry (the song is not shipped with the package).");

			var handle = Addressables.LoadAssetAsync<AudioClip>(PlayModeTestContentBuild.MusicAddress);
			yield return handle;
			Assert.AreEqual(AsyncOperationStatus.Succeeded, handle.Status, handle.OperationException?.ToString());

			var clip = handle.Result;
			Assert.AreEqual(AudioClipLoadType.Streaming, clip.loadType);
			Assert.Greater(clip.length, 60f, "full song");

			var source = new GameObject("StreamingAudioTest").AddComponent<AudioSource>();
			source.clip = clip;
			source.Play();
			yield return new WaitForSeconds(3f);

			Assert.AreEqual(AudioDataLoadState.Loaded, clip.loadState, "streamed audio data opened");
			Debug.Log($"[AddressablesEncryption tests] streaming clip '{clip.name}' {clip.length:F1}s, playing={source.isPlaying}, time={source.time:F2}s");
			Assert.IsTrue(source.isPlaying, "still playing");
			Assert.Greater(source.time, 1f, "playback advanced through the streamed data");

			// Seek near the end: the stream must serve a far offset too.
			source.time = clip.length - 5f;
			yield return new WaitForSeconds(1f);
			Assert.Greater(source.time, clip.length - 5f, "playback continues after a seek");

			Object.Destroy(source.gameObject);
			yield return null;
			handle.Release();
			AddressablesEncryption.ClearKey();
		}
	}
}
