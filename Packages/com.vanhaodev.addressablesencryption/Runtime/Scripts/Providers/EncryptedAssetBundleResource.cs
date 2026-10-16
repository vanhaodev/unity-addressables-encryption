using System;
using System.IO;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Loads one encrypted bundle through a decrypting stream (<c>AssetBundle.LoadFromStreamAsync</c>), so the
	/// bundle is never fully decrypted in memory and nothing plain is written to disk.<br/>
	/// Sources: local file; remote http(s) (<see cref="EncryptedBundleCache"/> first, else download straight into
	/// the cache file); other URLs such as jar: on Android (download; cached when the group has a hash, else kept
	/// encrypted in memory).<br/>
	/// Download-only requests (DownloadDependenciesAsync) just fill the cache. Supports WaitForCompletion.
	/// </summary>
	internal sealed partial class EncryptedAssetBundleResource : IAssetBundleResource
	{
		private ProvideHandle _handle;
		private AssetBundleRequestOptions _options;
		private string _path;
		private bool _downloadOnly;
		private string _cachePath;
		private string _tempPath;
		private bool _tempFileInUse;
		private bool _loadedFromCache;
		private bool _cacheRetried;

		private EncryptedBundleWebRequest _download;
		private AesCbcDecryptingReadStream _stream;
		private AssetBundleCreateRequest _loadRequest;
		private AssetBundle _bundle;
		private bool _completed;

		public AssetBundle GetAssetBundle() => _bundle;

		public void Start(ProvideHandle handle)
		{
			_handle = handle;
			_options = handle.Location.Data as AssetBundleRequestOptions;
			_downloadOnly = handle.Location.GetType().Name == "DownloadOnlyLocation"; // internal Addressables type
			ResourceLocationUtil.GetLoadInfo(handle.Location, handle.ResourceManager, out _, out _path);

			handle.SetProgressCallback(GetProgress);
			handle.SetDownloadProgressCallbacks(GetDownloadStatus);
			handle.SetWaitForCompletionCallback(WaitForCompletion);

			var isRemote = ResourceManagerConfig.IsPathRemote(_path);
			var isUrl = isRemote || _path.Contains("://") || _path.StartsWith("jar:", StringComparison.Ordinal);

			if (!isUrl)
			{
				// Local bundles are already on the device: nothing to download, nothing to cache.
				if (_downloadOnly)
					Succeed();
				else
					OpenFileAndLoad(_path, fromCache: false);
				return;
			}

#if !UNITY_WEBGL
			if (isRemote)
			{
				EncryptedBundleCache.ClearLegacyNativeCache(_options?.BundleName);
				_cachePath = EncryptedBundleCache.GetPath(_options?.BundleName, _options?.Hash);
			}
#endif

			if (_cachePath != null && File.Exists(_cachePath))
			{
				if (_downloadOnly)
					Succeed();
				else
					OpenFileAndLoad(_cachePath, fromCache: true);
			}
			else
			{
				Download();
			}
		}

		/// <summary>Unload the bundle and everything loaded from it, then close the stream behind it.</summary>
		public void Unload()
		{
			if (_bundle != null)
			{
				_bundle.Unload(true);
				_bundle = null;
			}
			CloseStream();
			if (_tempFileInUse)
			{
				_tempFileInUse = false;
				EncryptedBundleCache.TryDelete(_tempPath);
			}
		}

		private void CloseStream()
		{
			_stream?.Dispose();
			_stream = null;
		}

		private void OpenFileAndLoad(string filePath, bool fromCache)
		{
			_loadedFromCache = fromCache;
			AesCbcDecryptingReadStream stream;
			try
			{
				stream = EncryptedBundleStreamOpener.OpenFile(filePath, _path);
			}
			catch (Exception e)
			{
				if (TryRedownloadCorruptedCache(e.Message))
					return;
				Fail(e);
				return;
			}
			StartLoad(stream);
		}

		private void OpenMemoryAndLoad(byte[] encryptedBytes)
		{
			_loadedFromCache = false;
			AesCbcDecryptingReadStream stream;
			try
			{
				stream = EncryptedBundleStreamOpener.OpenMemory(encryptedBytes, _path);
			}
			catch (Exception e)
			{
				Fail(e);
				return;
			}
			StartLoad(stream);
		}

		private void StartLoad(AesCbcDecryptingReadStream stream)
		{
#if UNITY_EDITOR
			// Finished after leaving play mode: loading now would leak a bundle into the next session.
			if (!Application.isPlaying)
			{
				stream.Dispose();
				Fail(new OperationCanceledException("[AddressablesEncryption] Play mode ended while loading."));
				return;
			}
#endif
			_stream = stream;
			_loadRequest = AssetBundle.LoadFromStreamAsync(stream, Crc);
			_loadRequest.completed += _ => OnLoaded();
		}

		// Unity checks the CRC over the whole bundle (one extra decrypt pass). Same rules as the stock provider:
		// always for fresh files, for cached files only when the group asks for it.
		private uint Crc => _options == null || (_loadedFromCache && !_options.UseCrcForCachedBundle) ? 0 : _options.Crc;

		private void OnLoaded()
		{
			if (_completed || _loadRequest == null)
				return;

			_bundle = _loadRequest.assetBundle;
			_loadRequest = null;

			if (_bundle == null)
			{
				CloseStream();
				// A damaged cache file can pass the padding check (~1/256) and only fail here.
				if (TryRedownloadCorruptedCache("loaded bundle is invalid"))
					return;
				Fail(new InvalidDataException(
					$"[AddressablesEncryption] Decrypted bundle '{_path}' could not be loaded. Wrong key, CRC mismatch, or the bundle was built for another platform."));
				return;
			}

			_completed = true;
			if (ProvideHandleUtil.IsValid(_handle))
				_handle.Complete(this, true, null);
			else
				Unload(); // nobody can release it later
		}

		private void Succeed()
		{
			if (_completed)
				return;
			_completed = true;
			if (ProvideHandleUtil.IsValid(_handle))
				_handle.Complete<EncryptedAssetBundleResource>(null, true, null);
		}

		private void Fail(Exception error)
		{
			if (_completed)
				return;
			_completed = true;
			if (ProvideHandleUtil.IsValid(_handle))
				_handle.Complete<EncryptedAssetBundleResource>(null, false, error);
		}
	}
}
