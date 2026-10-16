using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Loads one encrypted bundle: read bytes → decrypt on a background thread → AssetBundle.LoadFromMemoryAsync.<br/>
	/// Plain file paths are read on the background thread; URLs (http, jar: on Android, file://) go through
	/// <see cref="EncryptedBundleWebRequest"/>. Supports WaitForCompletion (synchronous loads).
	/// Remote bundles are not cached (downloaded on every load).
	/// </summary>
	internal sealed class EncryptedAssetBundleResource : IAssetBundleResource
	{
		private ProvideHandle _handle;
		private string _path;

		private EncryptedBundleWebRequest _download;
		private Task<byte[]> _decryptTask;
		private AssetBundleCreateRequest _loadRequest;
		private AssetBundle _bundle;
		private bool _completed;

		public AssetBundle GetAssetBundle() => _bundle;

		public void Start(ProvideHandle handle)
		{
			_handle = handle;
			ResourceLocationUtil.GetLoadInfo(handle.Location, handle.ResourceManager, out _, out _path);
			var isUrl = _path.Contains("://") || _path.StartsWith("jar:", StringComparison.Ordinal);

			handle.SetProgressCallback(GetProgress);
			handle.SetWaitForCompletionCallback(WaitForCompletion);

			var id = _path;
			if (isUrl)
			{
				var options = handle.Location.Data as AssetBundleRequestOptions;
				_download = new EncryptedBundleWebRequest(_path, options, handle.ResourceManager, (bytes, error) =>
				{
					if (error != null)
						Fail(error);
					else
						StartDecrypt(() => AddressablesEncryption.DecryptBundle(bytes, id));
				});
				_download.Start();
			}
			else
			{
				StartDecrypt(() => AddressablesEncryption.DecryptBundle(File.ReadAllBytes(id), id));
			}
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

		// Download weighs 50%, decrypt + load the rest; local files skip straight to decrypt.
		private float GetProgress()
		{
			if (_completed)
				return 1f;
			if (_loadRequest != null)
				return 0.75f + _loadRequest.progress * 0.25f;
			if (_decryptTask != null)
				return 0.5f;
			return _download != null ? _download.Progress * 0.5f : 0f;
		}

		private void StartDecrypt(Func<byte[]> work)
		{
			_decryptTask = Task.Run(work);
			// Continue on the main thread: Provide always runs there, so the current context is Unity's.
			_decryptTask.ContinueWith(_ => OnDecrypted(), TaskScheduler.FromCurrentSynchronizationContext());
		}

		private void OnDecrypted()
		{
			// Already handled synchronously by WaitForCompletion, or the operation already ended.
			if (_completed || _loadRequest != null)
				return;

			try
			{
				if (_decryptTask.IsFaulted)
				{
					Fail(_decryptTask.Exception?.GetBaseException());
					return;
				}

#if UNITY_EDITOR
				// Decrypt finished after leaving play mode: loading now would leak a bundle into the next session.
				if (!Application.isPlaying)
				{
					Fail(new OperationCanceledException("[AddressablesEncryption] Play mode ended while loading."));
					return;
				}
#endif

				_loadRequest = AssetBundle.LoadFromMemoryAsync(_decryptTask.Result);
				_decryptTask = null; // drop the decrypted copy as soon as Unity owns the data
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
			if (_completed)
				return;

			_bundle = _loadRequest.assetBundle;
			_loadRequest = null;

			if (_bundle == null)
			{
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

		private void Fail(Exception error)
		{
			if (_completed)
				return;
			_completed = true;
			if (ProvideHandleUtil.IsValid(_handle))
				_handle.Complete<EncryptedAssetBundleResource>(null, false, error);
		}

		/// <summary>Drive every remaining stage synchronously on the main thread.</summary>
		private bool WaitForCompletion()
		{
			if (_completed)
				return true;

			// Download (with retries); its callback starts the decrypt task or fails.
			if (_decryptTask == null && _loadRequest == null)
				_download?.WaitForCompletion();

			if (_decryptTask != null && !_completed && _loadRequest == null)
			{
				try
				{
					_decryptTask.Wait();
				}
				catch (AggregateException)
				{
					// Reported by OnDecrypted through IsFaulted.
				}
				OnDecrypted();
			}

			if (_loadRequest != null && !_completed)
			{
				// Reading assetBundle on a pending request finishes the load synchronously.
				_ = _loadRequest.assetBundle;
				OnLoaded();
			}

			return _completed;
		}
	}
}
