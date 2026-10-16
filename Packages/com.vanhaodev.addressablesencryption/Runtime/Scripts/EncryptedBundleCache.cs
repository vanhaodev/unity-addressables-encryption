using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Disk cache for remote encrypted bundles (Unity's Caching only accepts plain bundles).<br/>
	/// - Files stay encrypted on disk: the cache is as safe from rippers as the CDN copy.<br/>
	/// - Names are hashed: &lt;root&gt;/&lt;sha(bundleName)&gt;/&lt;sha(bundleHash)&gt;, no group or bundle names on disk.<br/>
	/// - One version per bundle: storing a new hash deletes the older versions of that bundle.<br/>
	/// Enabled per group by "Use Asset Bundle Cache" (the catalog then carries the bundle hash).
	/// </summary>
	internal static class EncryptedBundleCache
	{
		private const string FileExtension = ".aaen";

		private static string _root;
		private static readonly HashSet<string> LegacyCleared = new();

		/// <summary>Cache folder. Resolved on the main thread (persistentDataPath is main-thread only). Tests may override it.</summary>
		internal static string Root
		{
			// GetFullPath: persistentDataPath uses '/', Path.Combine adds '\' on Windows; keep one canonical form.
			get => _root ??= Path.GetFullPath(Path.Combine(Application.persistentDataPath, "AddressablesEncryption", "Cache"));
			set => _root = value == null ? null : Path.GetFullPath(value);
		}

		// The binary catalog always stores a Hash128: a group with "Use Asset Bundle Cache" off gets all zeros.
		private const string NoHash = "00000000000000000000000000000000";

		/// <summary>Cache file path, or null when the bundle has no hash (cache disabled for its group). Main thread.</summary>
		public static string GetPath(string bundleName, string bundleHash)
		{
			if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(bundleHash) || bundleHash == NoHash)
				return null;
			return Path.Combine(Root, ShortHash(bundleName), ShortHash(bundleHash) + FileExtension);
		}

		public static bool IsCached(string bundleName, string bundleHash)
		{
			var path = GetPath(bundleName, bundleHash);
			return path != null && File.Exists(path);
		}

		/// <summary>
		/// Store encrypted bytes, replacing older versions of the same bundle. Thread-safe enough for concurrent
		/// stores of the same bundle: each writes its own temp file, the last move wins with identical content.
		/// </summary>
		public static void Store(string path, byte[] encryptedBytes)
		{
			var dir = Path.GetDirectoryName(path);
			Directory.CreateDirectory(dir);

			var temp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
			File.WriteAllBytes(temp, encryptedBytes);
			try
			{
				if (File.Exists(path))
					File.Delete(path);
				File.Move(temp, path); // never leaves a half-written cache file
			}
			catch (IOException) when (File.Exists(path))
			{
				File.Delete(temp); // a concurrent store of the same bundle won
			}

			// Compare file names only: GetFiles may format the directory part differently from 'path'.
			var keep = Path.GetFileName(path);
			foreach (var old in Directory.GetFiles(dir, "*" + FileExtension))
			{
				if (!string.Equals(Path.GetFileName(old), keep, StringComparison.OrdinalIgnoreCase))
					TryDelete(old);
			}
		}

		public static void TryDelete(string path)
		{
			try
			{
				if (File.Exists(path))
					File.Delete(path);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AddressablesEncryption] Could not delete cache file '{path}': {e.Message}");
			}
		}

		/// <summary>Delete every cached encrypted bundle.</summary>
		public static void Clear()
		{
			if (Directory.Exists(Root))
				Directory.Delete(Root, true);
		}

		/// <summary>
		/// A group that shipped unencrypted earlier may still have its plain bundle in Unity's native cache
		/// (kept up to 150 days by default). The native cache is keyed by bundle name, which encryption does not
		/// change, so clear that name once per session. Main thread.
		/// </summary>
		public static void ClearLegacyNativeCache(string bundleName)
		{
			if (string.IsNullOrEmpty(bundleName) || !LegacyCleared.Add(bundleName) || !Caching.ready)
				return;
			Caching.ClearAllCachedVersions(bundleName);
		}

		private static string ShortHash(string value)
		{
			using var sha = SHA256.Create();
			var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
			var sb = new StringBuilder(32);
			for (int i = 0; i < 16; i++)
				sb.Append(hash[i].ToString("x2"));
			return sb.ToString();
		}
	}
}
