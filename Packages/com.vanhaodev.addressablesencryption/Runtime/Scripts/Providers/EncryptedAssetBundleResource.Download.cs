using System;
using System.IO;
using UnityEngine;

namespace vanhaodev.addressablesencryption
{
	/// <summary>Download and cache side of <see cref="EncryptedAssetBundleResource"/>: remote and jar: bundles.</summary>
	internal sealed partial class EncryptedAssetBundleResource
	{
		private void Download()
		{
			// With a cache path the response goes straight to a temp file in the cache folder; otherwise to memory.
			_tempPath = _cachePath != null ? EncryptedBundleCache.GetTempPath(_cachePath) : null;
			_download = new EncryptedBundleWebRequest(_path, _tempPath, _options, _handle.ResourceManager, OnDownloaded);
			_download.Start();
		}

		private void OnDownloaded(byte[] bytes, Exception error)
		{
			if (error != null)
			{
				EncryptedBundleCache.TryDelete(_tempPath);
				Fail(error);
				return;
			}

			var encrypted = _tempPath != null ? EncryptedBundleFormat.IsEncryptedFile(_tempPath) : EncryptedBundleFormat.IsEncrypted(bytes);
			if (!encrypted)
			{
				EncryptedBundleCache.TryDelete(_tempPath);
				Fail(new InvalidDataException($"[AddressablesEncryption] Downloaded bundle '{_path}' is not encrypted. Rebuild with the encrypted build script."));
				return;
			}

			if (_tempPath != null)
			{
				var filePath = CommitDownload();
				if (_downloadOnly)
					Succeed();
				else
					OpenFileAndLoad(filePath, fromCache: false);
				return;
			}

			if (_downloadOnly)
				Succeed();
			else
				OpenMemoryAndLoad(bytes);
		}

		/// <summary>Move the finished download onto its cache name. A full disk must not fail the load: use the temp file then.</summary>
		private string CommitDownload()
		{
			try
			{
				EncryptedBundleCache.CommitTempFile(_tempPath, _cachePath);
				return _cachePath;
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AddressablesEncryption] Could not cache bundle '{_path}': {e.Message}");
				_tempFileInUse = true;
				return _tempPath;
			}
		}

		/// <summary>Corrupted (or stale-key) cache file: drop it and download once more. False if not applicable.</summary>
		private bool TryRedownloadCorruptedCache(string reason)
		{
			if (!_loadedFromCache || _cacheRetried)
				return false;

			_cacheRetried = true;
			_loadedFromCache = false;
			EncryptedBundleCache.TryDelete(_cachePath);
			Debug.LogWarning($"[AddressablesEncryption] Cached bundle '{_path}' unreadable ({reason}), downloading again.");
			Download();
			return true;
		}
	}
}
