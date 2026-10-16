using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using vanhaodev.addressablesencryption;

namespace vanhaodev.addressablesencryption.Samples
{
	/// <summary>
	/// Loads a picture and a song by address: draws the picture and plays the song (Load Type "Streaming" works
	/// from encrypted groups). Whether the bundle is encrypted is decided by the Addressables group (Encryption
	/// schema on/off), this code is the same either way. The song is optional: put any AudioClip in the group
	/// with address "Music".<br/>
	/// In the Editor, "Show Stream Monitor" adds <see cref="StreamReadMonitor"/>: live reads and memory while streaming.
	/// </summary>
	public class HanniDemo : MonoBehaviour
	{
		[SerializeField] private string m_Secret = "demo-secret";
		[SerializeField] private string m_Address = "Hanni";
		[SerializeField] private string m_MusicAddress = "Music";
		[Tooltip("Editor only: show every read from an encrypted bundle and the memory it uses, live.")]
		[SerializeField] private bool m_LogStreamReads = true;

		private Texture2D _texture;
		private string _status = "Loading...";
		private string _music = "";
#if UNITY_EDITOR
		private StreamReadMonitor _monitor;
#endif

		private void Awake()
		{
#if UNITY_EDITOR
			// Before anything loads: only bundles opened while the monitor listens are observed.
			if (m_LogStreamReads)
			{
				_monitor = gameObject.AddComponent<StreamReadMonitor>();
			}
#endif
		}

		private void Start()
		{
			// Must match the key in AddressablesEncryptionSettings. Harmless when the group is not encrypted.
			AddressablesEncryption.SetKey(m_Secret);

			Addressables.LoadAssetAsync<Texture2D>(m_Address).Completed += handle =>
			{
				if (handle.Status == AsyncOperationStatus.Succeeded)
				{
					_texture = handle.Result;
					_status = "Loaded";
				}
				else
				{
					_status = $"Failed: {handle.OperationException?.Message}";
					Debug.LogError($"[Demo] {_status}");
				}
			};

			PlayMusic();
		}

		private void PlayMusic()
		{
			// Only when the group has an entry with that address; the demo works without a song.
			Addressables.LoadResourceLocationsAsync(m_MusicAddress, typeof(AudioClip)).Completed += locations =>
			{
				var found = locations.Status == AsyncOperationStatus.Succeeded && locations.Result.Count > 0;
				// The song's bundle file, so the monitor can turn byte positions into time.
				var bundleFile = found && locations.Result[0].HasDependencies
					? System.IO.Path.GetFileName(locations.Result[0].Dependencies[0].InternalId)
					: null;
				locations.Release();
				if (!found)
					return;

				Addressables.LoadAssetAsync<AudioClip>(m_MusicAddress).Completed += handle =>
				{
					if (handle.Status != AsyncOperationStatus.Succeeded)
					{
						_music = $"Music failed: {handle.OperationException?.Message}";
						Debug.LogError($"[Demo] {_music}");
						return;
					}

					var source = gameObject.AddComponent<AudioSource>();
					source.clip = handle.Result;
#if UNITY_EDITOR
					if (_monitor != null && bundleFile != null)
						_monitor.SetSong(bundleFile, source);
#endif
					source.loop = true;
					source.Play();
					_music = $"Playing '{handle.Result.name}' ({handle.Result.loadType})";
				};
			};
		}

		private void OnGUI()
		{
			var pictureArea = new Rect(10, 10, Screen.width - 20, Screen.height - 70);
#if UNITY_EDITOR
			if (_monitor != null)
				pictureArea = StreamReadMonitor.PictureArea;
#endif
			if (_texture != null)
				GUI.DrawTexture(pictureArea, _texture, ScaleMode.ScaleToFit);

			GUI.Label(new Rect(10, Screen.height - 50, Screen.width, 25), _music);
			GUI.Label(new Rect(10, Screen.height - 30, Screen.width, 25), _status);
		}
	}
}
