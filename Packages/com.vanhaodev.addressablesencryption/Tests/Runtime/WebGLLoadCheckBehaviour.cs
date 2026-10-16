using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Debug = UnityEngine.Debug;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// Scene script of the WebGL check player (see WebGLPlayerBuildForEncryptionCheck): loads a local encrypted
	/// texture, a remote encrypted texture and a 16 MB encrypted texture through an encrypted catalog, logs
	/// "[WebGLCheck] ..." lines the browser console shows, and draws the pictures.
	/// </summary>
	public class WebGLLoadCheckBehaviour : MonoBehaviour
	{
		public const string RemoteAddress = "WebGLRemoteTexture";

		private readonly string[] _addresses = { PlayModeTestContentBuild.LocalAddress, RemoteAddress, PlayModeTestContentBuild.LargeAddress };
		private readonly Texture2D[] _textures = new Texture2D[3];
		private readonly string[] _status = { "waiting", "waiting", "waiting" };
		private string _init = "initializing";

		private IEnumerator Start()
		{
			AddressablesEncryption.SetKey(PlayModeTestContentBuild.Secret); // before Addressables initializes: the catalog is encrypted
			var watch = Stopwatch.StartNew();
			var init = Addressables.InitializeAsync();
			yield return init;
			_init = init.Status == AsyncOperationStatus.Succeeded ? $"catalog ok ({watch.ElapsedMilliseconds} ms)" : "init FAILED: " + init.OperationException?.Message;
			Debug.Log("[WebGLCheck] " + _init);

			for (int i = 0; i < _addresses.Length; i++)
				yield return Load(i);
			Debug.Log("[WebGLCheck] done");
		}

		private IEnumerator Load(int index)
		{
			var watch = Stopwatch.StartNew();
			var handle = Addressables.LoadAssetAsync<Texture2D>(_addresses[index]);
			yield return handle;
			if (handle.Status == AsyncOperationStatus.Succeeded)
			{
				_textures[index] = handle.Result;
				_status[index] = $"{_addresses[index]}: loaded {handle.Result.width}px in {watch.ElapsedMilliseconds} ms";
			}
			else
			{
				_status[index] = $"{_addresses[index]}: FAILED {handle.OperationException?.Message}";
			}
			Debug.Log("[WebGLCheck] " + _status[index]);
		}

		private void OnGUI()
		{
			var width = Screen.width / 3f;
			GUI.Label(new Rect(10, 10, Screen.width, 25), _init);
			for (int i = 0; i < _textures.Length; i++)
			{
				GUI.Label(new Rect(i * width + 10, 40, width - 20, 50), _status[i]);
				if (_textures[i] != null)
					GUI.DrawTexture(new Rect(i * width + 10, 90, width - 20, Screen.height - 100), _textures[i], ScaleMode.ScaleToFit);
			}
		}
	}
}
