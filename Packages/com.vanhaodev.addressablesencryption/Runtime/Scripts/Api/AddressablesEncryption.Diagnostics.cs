#if UNITY_EDITOR
using System;

namespace vanhaodev.addressablesencryption
{
	/// <summary>Editor-only diagnostics: watch encrypted bundles being read and decrypted. Compiled out of players.</summary>
	public static partial class AddressablesEncryption
	{
		/// <summary>
		/// Raised every time Unity reads decrypted data from an encrypted bundle:
		/// (bundle id, plaintext offset of the read, bytes read, bundle plaintext length).<br/>
		/// Unity reads on its loader and audio threads, so the handler runs on those threads: keep it short, do
		/// not touch Unity objects, hand the data to the main thread (e.g. a ConcurrentQueue). Costs nothing when
		/// nobody subscribes. Editor only: player builds do not contain it.
		/// </summary>
		public static event Action<string, long, int, long> BundleStreamRead;

		internal static bool HasBundleStreamReadListener => BundleStreamRead != null;

		internal static void RaiseBundleStreamRead(string bundleId, long position, int bytes, long length)
		{
			BundleStreamRead?.Invoke(bundleId, position, bytes, length);
		}
	}
}
#endif
