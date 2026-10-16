using System;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace vanhaodev.addressablesencryption
{
	/// <summary>Progress reporting and the synchronous path (WaitForCompletion) of <see cref="EncryptedAssetBundleResource"/>.</summary>
	internal sealed partial class EncryptedAssetBundleResource
	{
		// Download weighs 50%, the bundle load the rest; cached/local bundles skip the download.
		private float GetProgress()
		{
			if (_completed)
				return 1f;
			if (_loadRequest != null)
				return 0.5f + _loadRequest.progress * 0.5f;
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
				else if (_download != null && !_download.IsDone)
				{
					_download.WaitForCompletion(); // its callback opens the stream and starts the load, or fails
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
