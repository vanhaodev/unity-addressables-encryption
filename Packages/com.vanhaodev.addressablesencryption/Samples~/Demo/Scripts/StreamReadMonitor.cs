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
	/// Editor only. While the demo runs: one memory bar per bundle (see <see cref="StreamMemoryBar"/>) and one live
	/// timeline, newest first: green lines = data read (decrypted), red lines = decrypted data dropped from memory.<br/>
	/// The decrypting stream keeps one 128 KB window of decrypted data per bundle. When Unity reads outside it, the
	/// old window is thrown away and the next one is decrypted in its place: played audio is never kept.
	/// This monitor follows the same rule to show when that happens. Added by <see cref="HanniDemo"/>.<br/>
	/// For a song (see <see cref="SetDuration"/>) each line also shows the matching time range, estimated from the
	/// byte position (exact enough for constant-bitrate audio).
	/// </summary>
	public class StreamReadMonitor : MonoBehaviour
	{
		private const int MaxLines = 16;
		private const long Window = 128 * 1024; // decrypted window of the stream (encryptionmanager)
		private const string Green = "#40cc5a", Red = "#ff5050";

		private struct Read { public string Bundle; public long Position; public int Bytes; public long Length; }

		// Reads arrive on Unity's loader and audio threads: queue them, use them on the main thread.
		private readonly ConcurrentQueue<Read> _incoming = new ConcurrentQueue<Read>();
		private readonly List<string> _timeline = new List<string>(); // newest first
		private readonly Dictionary<string, long> _windowStart = new Dictionary<string, long>();
		private readonly Dictionary<string, StreamMemoryBar> _bars = new Dictionary<string, StreamMemoryBar>();
		private readonly Dictionary<string, float> _durations = new Dictionary<string, float>();
		private readonly Dictionary<string, long> _lastEnd = new Dictionary<string, long>();
		private readonly Dictionary<string, long> _lengths = new Dictionary<string, long>();
		private AudioSource _song;
		private int _readCount, _freedCount;

		/// <summary>Area the picture may use, so it does not overlap the bars and the timeline.</summary>
		public static Rect PictureArea => new Rect(10, 10, Screen.width - 20, Screen.height * 0.3f);

		/// <summary>
		/// The song stored in that bundle file: its lines then show time ranges too, and the title shows where the
		/// song is actually playing (Unity reads ahead of it).
		/// </summary>
		public void SetSong(string bundleFileName, AudioSource source)
		{
			_durations[bundleFileName] = source.clip.length;
			_song = source;
		}

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
				// Outside the current window: the stream drops it and decrypts a new one from this position.
				var hasWindow = _windowStart.TryGetValue(read.Bundle, out var start);
				var moved = !hasWindow || read.Position < start || read.Position >= start + Window;
				if (moved && hasWindow)
				{
					_freedCount++;
					var end = System.Math.Min(start + Window, read.Length);
					Bar(read).Free(start, end);
					Add(Red, $"{Short(read.Bundle)}  freed {(end - start) / 1024.0,5:F0} KB  {Range(read.Bundle, start, end, read.Length)}");
				}
				if (moved)
				{
					var newStart = read.Position - read.Position % 16; // windows start on an AES block
					_windowStart[read.Bundle] = newStart;
					Bar(read).Hold(newStart, System.Math.Min(newStart + Window, read.Length));
				}

				_readCount++;
				// The audio decoder sometimes steps back a little (frame resync, overlapping reads): mark it.
				var back = _lastEnd.TryGetValue(read.Bundle, out var lastEnd) && read.Position < lastEnd && lastEnd - read.Position < Window
					? $"   ↩ back {(lastEnd - read.Position) / 1024.0:F1} KB" : "";
				_lastEnd[read.Bundle] = read.Position + read.Bytes;
				Add(Green, $"{Short(read.Bundle)}  read  {read.Bytes / 1024.0,5:F1} KB  {Range(read.Bundle, read.Position, read.Position + read.Bytes, read.Length)}{back}");
			}
		}

		private void OnGUI()
		{
			var style = new GUIStyle(GUI.skin.label) { richText = true };

			// Middle: one bar per bundle.
			var y = Screen.height * 0.3f + 20;
			foreach (var pair in _bars)
				y += pair.Value.Draw(new Rect(10, y, Screen.width - 20, 80), Short(pair.Key), MemoryLine(pair.Key), style);

			// Bottom: the timeline.
			var area = new Rect(10, y + 10, Screen.width - 20, Screen.height - y - 80);
			GUI.Box(area, GUIContent.none);
			var title = $"Timeline, newest first:  <color={Green}>read {_readCount}</color>   <color={Red}>freed {_freedCount}</color>";
			if (_song != null && _song.clip != null)
				title += $"      song playing at {Clock(_song.time)} / {Clock(_song.clip.length)} (reads run ahead of it)";
			// One-line legend so the panel explains itself.
			title += "\n<size=11>Unity reads small pieces (green). The stream decrypts 128 KB at a time and keeps only that window: " +
				"when a read leaves it, the whole window is dropped (red). ↩ = the audio decoder stepped back a little.</size>";
			GUI.Label(new Rect(area.x + 8, area.y + 4, area.width - 16, area.height - 8), title + "\n" + string.Join("\n", _timeline), style);
		}

		/// <summary>
		/// What this bundle costs in RAM right now: the stream's two fixed 128 KB buffers (decrypted window and its
		/// ciphertext), and for the song the AudioClip's own memory as measured by Unity.
		/// </summary>
		private string MemoryLine(string bundle)
		{
			if (!_windowStart.TryGetValue(bundle, out var start) || !_lengths.TryGetValue(bundle, out var length))
				return "";
			// The stream allocates both buffers at full size once, whatever part of them is filled.
			var filled = System.Math.Min(Window, length - start);
			var total = Window * 2 + 16; // decrypted buffer + ciphertext buffer (one extra AES block for chaining)
			var text = $"in RAM now:  decrypted {Window / 1024} KB (filled {filled / 1024.0:F0} KB)  + encrypted buffer {Window / 1024} KB";
			if (_song != null && _song.clip != null && _durations.ContainsKey(bundle))
			{
				var clip = Profiler.GetRuntimeMemorySizeLong(_song.clip);
				total += clip;
				text += $"  + Unity audio clip {clip / 1024.0:F0} KB";
			}
			return text + $"  =  <b>{total / 1048576.0:F2} MB</b>  (bundle {length / 1048576.0:F2} MB)";
		}

		private StreamMemoryBar Bar(Read read)
		{
			if (!_bars.TryGetValue(read.Bundle, out var bar))
				_bars[read.Bundle] = bar = new StreamMemoryBar(read.Length);
			_lengths[read.Bundle] = read.Length;
			return bar;
		}

		private void Add(string color, string line)
		{
			_timeline.Insert(0, $"<color={color}>{line}</color>");
			if (_timeline.Count > MaxLines)
				_timeline.RemoveAt(_timeline.Count - 1);
		}

		/// <summary>"12.4% - 18.5%", plus "(0:16 - 0:24)" when the bundle holds a song of known length.</summary>
		private string Range(string bundle, long start, long end, long length)
		{
			if (length <= 0)
				return "";
			var text = $"{start * 100.0 / length,5:F1}% - {end * 100.0 / length,5:F1}%";
			if (_durations.TryGetValue(bundle, out var seconds))
				text += $"   ({Clock(start * seconds / length)} - {Clock(end * seconds / length)})";
			return text;
		}

		private static string Clock(double seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

		private static string Short(string bundle) => bundle.Length > 14 ? bundle.Substring(0, 8) + ".." + bundle.Substring(bundle.Length - 5) : bundle;
	}
}
#endif
