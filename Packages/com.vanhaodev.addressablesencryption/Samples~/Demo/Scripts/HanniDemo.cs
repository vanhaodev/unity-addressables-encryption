using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
	/// Editor only: "Log Stream Reads" shows every read Unity makes from an encrypted bundle, live: the picture's bundle is read
	/// once while it loads, a streamed song keeps being read in small pieces while it plays.
	/// </summary>
	public class HanniDemo : MonoBehaviour
	{
		[SerializeField] private string m_Secret = "demo-secret";
		[SerializeField] private string m_Address = "Hanni";
		[SerializeField] private string m_MusicAddress = "Music";
		[Tooltip("Editor only: show every read Unity makes from an encrypted bundle, live.")]
		[SerializeField] private bool m_LogStreamReads = true;

		private const int MaxLogLines = 14;

		// Reads are reported on Unity's loader and audio threads: queue them, show them on the main thread.
		private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
		private readonly List<string> _logLines = new List<string>(); // newest first
		private int _readCount;
		private Texture2D _texture;
		private string _status = "Loading...";
		private string _music = "";

		private void Awake()
		{
#if UNITY_EDITOR
			// Subscribe before anything loads: only bundles opened while someone listens are observed.
			if (m_LogStreamReads)
				AddressablesEncryption.BundleStreamRead += OnBundleStreamRead;
#endif
		}

		private void OnDestroy()
		{
#if UNITY_EDITOR
			AddressablesEncryption.BundleStreamRead -= OnBundleStreamRead;
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
					source.loop = true;
					source.Play();
					_music = $"Playing '{handle.Result.name}' ({handle.Result.loadType})";
				};
			};
		}

#if UNITY_EDITOR
		// Loader / audio thread: no Unity API here except Debug.Log, which is thread-safe.
		private void OnBundleStreamRead(string bundleId, long position, int bytes, long length)
		{
			var percent = length > 0 ? (position + bytes) * 100.0 / length : 0;
			var line = $"{Path.GetFileName(bundleId)}  read {bytes / 1024.0,7:F1} KB at {position / 1024.0,9:F1} KB  ({percent,5:F1}%)";
			_incoming.Enqueue(line);
			Debug.Log("[Demo stream] " + line);
		}
#endif

		private void Update()
		{
			while (_incoming.TryDequeue(out var line))
			{
				_readCount++;
				_logLines.Insert(0, line); // newest on top, always visible
				if (_logLines.Count > MaxLogLines)
					_logLines.RemoveAt(_logLines.Count - 1);
			}
		}

		private void OnGUI()
		{
			// Size the log box from the real line height (it changes with DPI and skin), title line included.
			var lineHeight = Mathf.CeilToInt(GUI.skin.label.lineHeight) + 1;
			var logHeight = (MaxLogLines + 1) * lineHeight + 12;
			if (_texture != null)
				GUI.DrawTexture(new Rect(10, 10, Screen.width - 20, Screen.height - 80 - logHeight), _texture, ScaleMode.ScaleToFit);

			if (m_LogStreamReads && Application.isEditor)
			{
				var area = new Rect(10, Screen.height - 60 - logHeight, Screen.width - 20, logHeight);
				GUI.Box(area, GUIContent.none);
				GUI.Label(new Rect(area.x + 8, area.y + 4, area.width - 16, area.height - 8),
					$"Encrypted stream reads (live, newest first) - {_readCount} reads\n" + string.Join("\n", _logLines));
			}

			GUI.Label(new Rect(10, Screen.height - 50, Screen.width, 25), _music);
			GUI.Label(new Rect(10, Screen.height - 30, Screen.width, 25), _status);
		}
	}
}
