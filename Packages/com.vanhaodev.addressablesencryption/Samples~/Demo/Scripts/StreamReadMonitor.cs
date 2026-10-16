#if UNITY_EDITOR
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using vanhaodev.addressablesencryption;

namespace vanhaodev.addressablesencryption.Samples
{
	/// <summary>
	/// Editor only. Two live logs while the demo runs:<br/>
	/// - left: every read Unity makes from an encrypted bundle (what gets decrypted, at which %);<br/>
	/// - right, in red: every decrypted piece that is dropped from memory (at which %).<br/>
	/// The decrypting stream keeps one 128 KB window of decrypted data per bundle. When Unity reads outside it, the
	/// old window is thrown away and the next one is decrypted in its place: played audio is never kept.
	/// This monitor follows the same rule to show when that happens. Added by <see cref="HanniDemo"/>.
	/// </summary>
	public class StreamReadMonitor : MonoBehaviour
	{
		private const int MaxLines = 14;
		private const long Window = 128 * 1024; // decrypted window of the stream (encryptionmanager)

		private struct Read { public string Bundle; public long Position; public int Bytes; public long Length; }

		// Reads arrive on Unity's loader and audio threads: queue them, use them on the main thread.
		private readonly ConcurrentQueue<Read> _incoming = new ConcurrentQueue<Read>();
		private readonly List<string> _loaded = new List<string>();   // newest first
		private readonly List<string> _released = new List<string>(); // newest first
		private readonly Dictionary<string, long> _windowStart = new Dictionary<string, long>();
		private int _loadedCount, _releasedCount;

		/// <summary>Area the picture may use, so it does not overlap the two logs.</summary>
		public static Rect PictureArea => new Rect(10, 10, Screen.width - 20, Screen.height * 0.45f);

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
				_loadedCount++;
				Add(_loaded, $"{Short(read.Bundle)}  read {read.Bytes / 1024.0,6:F1} KB at {Percent(read.Position, read.Length)}");

				// Outside the current window: the stream drops it and decrypts a new one from this position.
				var hasWindow = _windowStart.TryGetValue(read.Bundle, out var start);
				if (hasWindow && read.Position >= start && read.Position < start + Window)
					continue;
				if (hasWindow)
				{
					_releasedCount++;
					var end = System.Math.Min(start + Window, read.Length);
					var line = $"{Short(read.Bundle)}  freed {(end - start) / 1024.0,5:F0} KB at {Percent(start, read.Length)} - {Percent(end, read.Length)}";
					Add(_released, $"<color=#ff5050>{line}</color>");
					Debug.Log("[Demo stream] " + line);
				}
				_windowStart[read.Bundle] = read.Position - read.Position % 16; // windows start on an AES block
			}
		}

		private void OnGUI()
		{
			var top = Screen.height * 0.45f + 20;
			var height = Screen.height - top - 60;
			var half = (Screen.width - 30) / 2f;
			var style = new GUIStyle(GUI.skin.label) { richText = true };

			Draw(new Rect(10, top, half, height), $"Decrypted (read), newest first - {_loadedCount}", _loaded, style);
			Draw(new Rect(20 + half, top, half, height), $"<color=#ff5050>Freed from memory</color>, newest first - {_releasedCount}", _released, style);
		}

		private static void Draw(Rect area, string title, List<string> lines, GUIStyle style)
		{
			GUI.Box(area, GUIContent.none);
			GUI.Label(new Rect(area.x + 8, area.y + 4, area.width - 16, area.height - 8), title + "\n" + string.Join("\n", lines), style);
		}

		private static void Add(List<string> lines, string line)
		{
			lines.Insert(0, line);
			if (lines.Count > MaxLines)
				lines.RemoveAt(lines.Count - 1);
		}

		private static string Percent(long position, long length) => length > 0 ? $"{position * 100.0 / length,5:F1}%" : "?";

		private static string Short(string bundle) => bundle.Length > 14 ? bundle.Substring(0, 8) + "…" + bundle.Substring(bundle.Length - 5) : bundle;
	}
}
#endif
