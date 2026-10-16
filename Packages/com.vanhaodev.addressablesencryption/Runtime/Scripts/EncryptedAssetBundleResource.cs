using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Loads one encrypted bundle: get bytes → background work (decrypt, cache) → AssetBundle.LoadFromMemoryAsync.<br/>
	/// Sources: local file (read on the background thread), remote http(s) (<see cref="EncryptedBundleCache"/> first,
	/// then <see cref="EncryptedBundleWebRequest"/>), other URLs such as jar: on Android (web request, no cache).<br/>
	/// Download-only requests (DownloadDependenciesAsync) just fill the cache. Supports WaitForCompletion.
	/// </summary>
	internal sealed class EncryptedAssetBundleResource : IAssetBundleResource
	{
		private ProvideHandle _handle;
		private AssetBundleRequestOptions _options;
		private string _path;
		private bool _downloadOnly;
		private string _cachePath;
		private bool _workFromCache;
		private bool _cacheRetried;

		private EncryptedBundleWebRequest _download;
		private Task<byte[]> _workTask;
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
					StartWork(ReadLocalAndDecrypt(_path));
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
				LoadFromCache();
			else
				Download();
		}

		/// <summary>Unload the bundle and everything loaded from it.</summary>
		public void Unload()
		{
			if (_bundle != null)
			{
				_bundle.Unload(true);
				_bundle = null;
			}
		}

		private void LoadFromCache()
		{
			if (_downloadOnly)
			{
				Succeed();
				return;
			}
			StartWork(ReadLocalAndDecrypt(_cachePath), fromCache: true);
		}

		private void Download()
		{
			_download = new EncryptedBundleWebRequest(_path, _options, _handle.ResourceManager, OnDownloaded);
			_download.Start();
		}

		private void OnDownloaded(byte[] bytes, Exception error)
		{
			if (error != null)
			{
				Fail(error);
				return;
			}

			var id = _path;
			var cachePath = _cachePath;
			if (_downloadOnly)
			{
				StartWork(() =>
				{
					if (!EncryptedBundleFormat.IsEncrypted(bytes))
						throw new InvalidDataException($"[AddressablesEncryption] Downloaded bundle '{id}' is not encrypted. Rebuild with the encrypted build script.");
					TryStore(cachePath, bytes);
					return null;
				});
				return;
			}

			StartWork(() =>
			{
				var plain = AddressablesEncryption.DecryptBundle(bytes, id);
				TryStore(cachePath, bytes); // only cache what decrypted fine
				return plain;
			});
		}

		private static Func<byte[]> ReadLocalAndDecrypt(string path)
		{
			return () => AddressablesEncryption.DecryptBundle(File.ReadAllBytes(path), path);
		}

		// Background thread. A full disk must not fail the load: the bundle is in memory already.
		private static void TryStore(string cachePath, byte[] encryptedBytes)
		{
			if (cachePath == null)
				return;
			try
			{
				EncryptedBundleCache.Store(cachePath, encryptedBytes);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[AddressablesEncryption] Could not cache bundle: {e.Message}");
			}
		}

		private void StartWork(Func<byte[]> work, bool fromCache = false)
		{
			_workFromCache = fromCache;
			var task = Task.Run(work);
			_workTask = task;
			// Continue on the main thread (Provide always runs there); ignore tasks already handled or replaced.
			task.ContinueWith(t =>
			{
				if (t == _workTask)
					OnWorkDone();
			}, TaskScheduler.FromCurrentSynchronizationContext());
		}

		private void OnWorkDone()
		{
			if (_completed || _loadRequest != null || _workTask == null)
				return;

			var task = _workTask;
			try
			{
				if (task.IsFaulted)
				{
					var error = task.Exception?.GetBaseException();
					if (TryRedownloadCorruptedCache(error?.Message))
						return;
					Fail(error);
					return;
				}

				if (_downloadOnly)
				{
					Succeed();
					return;
				}

#if UNITY_EDITOR
				// Finished after leaving play mode: loading now would leak a bundle into the next session.
				if (!Application.isPlaying)
				{
					Fail(new OperationCanceledException("[AddressablesEncryption] Play mode ended while loading."));
					return;
				}
#endif

				_loadRequest = AssetBundle.LoadFromMemoryAsync(task.Result);
				_workTask = null; // drop the decrypted copy as soon as Unity owns the data
				_loadRequest.completed += _ => OnLoaded();
			}
			catch (Exception e)
			{
				// An exception in a ContinueWith continuation is swallowed: report it or the operation hangs.
				Fail(e);
			}
		}

		private void OnLoaded()
		{
			if (_completed || _loadRequest == null)
				return;

			_bundle = _loadRequest.assetBundle;
			_loadRequest = null;

			if (_bundle == null)
			{
				// A damaged cache file can pass the padding check (~1/256) and only fail here.
				if (TryRedownloadCorruptedCache("loaded bundle is invalid"))
					return;
				Fail(new InvalidDataException(
					$"[AddressablesEncryption] Decrypted bundle '{_path}' could not be loaded. Wrong key, or the bundle was built for another platform."));
				return;
			}

			_completed = true;
			if (ProvideHandleUtil.IsValid(_handle))
				_handle.Complete(this, true, null);
			else
				Unload(); // nobody can release it later
		}

		/// <summary>Corrupted (or stale-key) cache file: drop it and download once more. False if not applicable.</summary>
		private bool TryRedownloadCorruptedCache(string reason)
		{
			if (!_workFromCache || _cacheRetried)
				return false;

			_cacheRetried = true;
			_workFromCache = false;
			_workTask = null;
			EncryptedBundleCache.TryDelete(_cachePath);
			Debug.LogWarning($"[AddressablesEncryption] Cached bundle '{_path}' unreadable ({reason}), downloading again.");
			Download();
			return true;
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

		// Download weighs 50%, background work + load the rest; cached/local bundles skip the download.
		private float GetProgress()
		{
			if (_completed)
				return 1f;
			if (_loadRequest != null)
				return 0.75f + _loadRequest.progress * 0.25f;
			if (_workTask != null)
				return 0.5f;
			return _download != null ? _download.Progress * 0.5f : 0f;
		}

		// Bytes only for real downloads; matches AddressablesEncryption.GetDownloadSizeAsync (0 when cached or local).
		private DownloadStatus GetDownloadStatus()
		{
			var total = _download != null && _options != null ? _options.BundleSize : 0;
			var downloaded = _download != null ? Math.Min(_download.DownloadedBytes, total) : 0;
			return new DownloadStatus { TotalBytes = total, DownloadedBytes = downloaded, IsDone = _completed };
		}

		/// <summary>Drive every remaining stage synchronously on the main thread.</summary>
		private bool WaitForCompletion()
		{
			while (!_completed)
			{
				if (_loadRequest != null)
				{
					// Reading assetBundle on a pending request finishes the load synchronously.
					_ = _loadRequest.assetBundle;
					OnLoaded();
				}
				else if (_workTask != null)
				{
					try
					{
						_workTask.Wait();
					}
					catch (AggregateException)
					{
						// Reported by OnWorkDone through IsFaulted.
					}
					OnWorkDone();
				}
				else if (_download != null && !_download.IsDone)
				{
					_download.WaitForCompletion(); // its callback starts the work or fails
				}
				else
				{
					break; // nothing in flight
				}
			}
			return _completed;
		}
	}
}
