#if UNITY_EDITOR
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using vanhaodev.addressablesencryption;

namespace vanhaodev.addressablesencryption.Samples
{
	/// <summary>
	/// Editor only. Shows how encrypted bundles are read while the demo runs:<br/>
	/// - left, bottom: every read Unity makes from an encrypted bundle, newest first;<br/>
	/// - right: per bundle, how much was read in total versus how much decrypted data is held right now.<br/>
	/// The decrypting stream keeps ONE 128 KB window of plaintext (plus its ciphertext) and overwrites it on every
	/// refill, so what has been played is never kept: memory stays flat however long the song is. The managed heap
	/// line shows it live. Added by <see cref="HanniDemo"/>; it must exist before the bundles are opened.
	/// </summary>
	public class StreamReadMonitor : MonoBehaviour
	{
		private const int MaxLogLines = 12;
		private const int WindowKb = 128; // plaintext chunk size of the decrypting stream (encryptionmanager)

		private struct Read { public string Bundle; public long Position; public int Bytes; public long Length; }

		private class BundleStats { public long Length, TotalRead, LastPosition; public int Reads; }

		// Reads arrive on Unity's loader and audio threads: queue them, use them on the main thread.
		private readonly ConcurrentQueue<Read> _incoming = new ConcurrentQueue<Read>();
		private readonly List<string> _log = new List<string>(); // newest first
		private readonly Dictionary<string, BundleStats> _bundles = new Dictionary<string, BundleStats>();
		private int _readCount;
		private long _heapNow, _heapMin = long.MaxValue, _heapMax;
		private float _nextHeapSample;

		/// <summary>Left area the picture may use, so it does not overlap the log and the panel.</summary>
		public static Rect PictureArea => new Rect(10, 10, Screen.width * 0.6f - 20, Screen.height * 0.5f);

		private void Awake() => AddressablesEncryption.BundleStreamRead += OnRead;
		private void OnDestroy() => AddressablesEncryption.BundleStreamRead -= OnRead;

		private void OnRead(string bundleId, long position, int bytes, long length)
		{
			_incoming.Enqueue(new Read { Bundle = Path.GetFileName(bundleId), Position = position, Bytes = bytes, Length = length });
		}

		private void Update()
		{
			while (_incoming.TryDequeue(out var read))
			{
				_readCount++;
				if (!_bundles.TryGetValue(read.Bundle, out var stats))
					_bundles[read.Bundle] = stats = new BundleStats();
				stats.Length = read.Length;
				stats.TotalRead += read.Bytes;
				stats.LastPosition = read.Position + read.Bytes;
				stats.Reads++;

				var percent = read.Length > 0 ? stats.LastPosition * 100.0 / read.Length : 0;
				var line = $"{Short(read.Bundle)}  {read.Bytes / 1024.0,6:F1} KB at {read.Position / 1024.0,8:F0} KB ({percent,5:F1}%)";
				_log.Insert(0, line);
				if (_log.Count > MaxLogLines)
					_log.RemoveAt(_log.Count - 1);
				Debug.Log("[Demo stream] " + line);
			}

			if (Time.unscaledTime >= _nextHeapSample)
			{
				_nextHeapSample = Time.unscaledTime + 0.5f;
				_heapNow = Profiler.GetMonoUsedSizeLong();
				if (Time.unscaledTime > 3f) // skip the startup allocations
				{
					_heapMin = System.Math.Min(_heapMin, _heapNow);
					_heapMax = System.Math.Max(_heapMax, _heapNow);
				}
			}
		}

		private void OnGUI()
		{
			var line = Mathf.CeilToInt(GUI.skin.label.lineHeight) + 1;

			// Left, under the picture: the live read log.
			var log = new Rect(10, Screen.height * 0.5f + 20, Screen.width * 0.6f - 20, Screen.height * 0.5f - 80);
			GUI.Box(log, GUIContent.none);
			GUI.Label(Inset(log), $"Encrypted stream reads (live, newest first) - {_readCount} reads\n" + string.Join("\n", _log));

			// Right: what is read versus what is held.
			var panel = new Rect(Screen.width * 0.6f, 10, Screen.width * 0.4f - 10, Screen.height - 70);
			GUI.Box(panel, GUIContent.none);
			var text = "Memory while streaming\n\n";
			foreach (var pair in _bundles)
			{
				var s = pair.Value;
				var held = Mathf.Min(WindowKb, s.Length / 1024f);
				text += $"{Short(pair.Key)}\n" +
					$"  size            {s.Length / 1048576.0:F2} MB\n" +
					$"  read so far     {s.TotalRead / 1048576.0:F2} MB in {s.Reads} reads\n" +
					$"  now at          {s.LastPosition / 1048576.0:F2} MB\n" +
					$"  decrypted held  {held:F0} KB (one window, reused)\n\n";
			}
			var heapRange = _heapMax > 0 ? $"{_heapMin / 1048576.0:F1} - {_heapMax / 1048576.0:F1} MB" : "measuring...";
			text += $"Managed heap now  {_heapNow / 1048576.0:F1} MB\nMin - max (after start)  {heapRange}\n\n" +
				"Played audio is not kept: each new piece is\ndecrypted over the same window, so memory\nstays flat however long the song is.";
			GUI.Label(Inset(panel), text);
		}

		private static Rect Inset(Rect r) => new Rect(r.x + 8, r.y + 4, r.width - 16, r.height - 8);

		private static string Short(string bundle) => bundle.Length > 14 ? bundle.Substring(0, 8) + "…" + bundle.Substring(bundle.Length - 5) : bundle;
	}
}
#endif
