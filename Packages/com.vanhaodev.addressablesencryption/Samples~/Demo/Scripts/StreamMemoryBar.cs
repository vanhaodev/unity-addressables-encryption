#if UNITY_EDITOR
using UnityEngine;

namespace vanhaodev.addressablesencryption.Samples
{
	/// <summary>
	/// Editor only. One bundle drawn as a bar, left = start of the file, right = end:
	/// green = decrypted and held in memory now, red = decrypted earlier and freed, light purple = not read yet.
	/// The file is split into 16 KB slots; <see cref="StreamReadMonitor"/> marks them as the stream moves its window.
	/// </summary>
	public class StreamMemoryBar
	{
		private const int SlotSize = 16 * 1024;
		private const byte NotYet = 0, Held = 1, Freed = 2;

		private static readonly Color Green = new Color(0.25f, 0.8f, 0.35f);
		private static readonly Color Red = new Color(0.9f, 0.25f, 0.25f);
		private static readonly Color Purple = new Color(0.78f, 0.7f, 0.95f);

		private readonly byte[] _slots;
		private readonly long _length;

		public StreamMemoryBar(long length)
		{
			_length = length;
			_slots = new byte[Mathf.Max(1, (int)((length + SlotSize - 1) / SlotSize))];
		}

		public void Hold(long start, long end) => Mark(start, end, Held);
		public void Free(long start, long end) => Mark(start, end, Freed);

		private void Mark(long start, long end, byte state)
		{
			var first = (int)(start / SlotSize);
			var last = (int)((Mathf.Max(start, end - 1)) / SlotSize);
			for (int i = Mathf.Max(0, first); i <= last && i < _slots.Length; i++)
				_slots[i] = state;
		}

		/// <summary>Bar + "held / freed / not read" percentages under it. Returns the height used.</summary>
		public float Draw(Rect area, string name, GUIStyle style)
		{
			int held = 0, freed = 0;
			foreach (var s in _slots)
			{
				if (s == Held) held++;
				else if (s == Freed) freed++;
			}

			GUI.Label(new Rect(area.x, area.y, area.width, 20), $"{name}  ({_length / 1048576.0:F2} MB)", style);
			var bar = new Rect(area.x, area.y + 20, area.width, 16);

			// Draw runs of equal slots as single rectangles.
			var previous = GUI.color;
			var slotWidth = bar.width / _slots.Length;
			for (int i = 0; i < _slots.Length;)
			{
				var state = _slots[i];
				var j = i + 1;
				while (j < _slots.Length && _slots[j] == state)
					j++;
				GUI.color = state == Held ? Green : state == Freed ? Red : Purple;
				GUI.DrawTexture(new Rect(bar.x + i * slotWidth, bar.y, (j - i) * slotWidth, bar.height), Texture2D.whiteTexture);
				i = j;
			}
			GUI.color = previous;

			var total = (float)_slots.Length;
			GUI.Label(new Rect(area.x, bar.yMax + 2, area.width, 20),
				$"<color=#40cc5a>held {held * 100 / total:F1}%</color>   <color=#ff5050>freed {freed * 100 / total:F1}%</color>   " +
				$"<color=#c8b4f2>not read {(total - held - freed) * 100 / total:F1}%</color>", style);
			return 60;
		}
	}
}
#endif
