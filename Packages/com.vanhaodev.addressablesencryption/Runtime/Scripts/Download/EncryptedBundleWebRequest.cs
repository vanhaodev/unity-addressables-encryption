using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;
using Debug = UnityEngine.Debug;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Downloads the raw (still encrypted) bytes of a bundle URL with the group's request options:<br/>
	/// - Timeout = seconds WITHOUT progress (same meaning as the stock provider), not total duration<br/>
	/// - RetryCount, only for errors worth retrying (no retry on 4xx)<br/>
	/// - ResourceManager certificate handler and WebRequestOverride<br/>
	/// Calls onDone exactly once with the bytes (memory mode) or null (file mode, data is at downloadToPath), or an exception.
	/// In file mode the caller deletes the file after a failure.
	/// </summary>
	internal sealed class EncryptedBundleWebRequest : IUpdateReceiver
	{
		private const int WaitSleepMs = 1;

		private readonly string _url;
		private readonly string _downloadToPath;
		private readonly AssetBundleRequestOptions _options;
		private readonly ResourceManager _rm;
		private readonly Action<byte[], Exception> _onDone;

		private UnityWebRequest _request;
		private int _retries;
		private float _idleSeconds;
		private ulong _lastDownloadedBytes;
		private bool _timedOut;
		private bool _done;

		/// <param name="downloadToPath">Write the response straight to this file (no full copy in memory), or null to keep it in memory.</param>
		public EncryptedBundleWebRequest(string url, string downloadToPath, AssetBundleRequestOptions options, ResourceManager rm, Action<byte[], Exception> onDone)
		{
			_url = url;
			_downloadToPath = downloadToPath;
			_options = options;
			_rm = rm;
			_onDone = onDone;
		}

		public float Progress => _done ? 1f : _request?.downloadProgress ?? 0f;

		public bool IsDone => _done;

		private long _finalBytes;

		public long DownloadedBytes => _done ? _finalBytes : (long)(_request?.downloadedBytes ?? 0);

		private int TimeoutSeconds => _options?.Timeout ?? 0;

		public void Start()
		{
			if (TimeoutSeconds > 0)
				_rm.AddUpdateReceiver(this);
			Send();
		}

		/// <summary>Block the main thread until the download finished (including retries).</summary>
		public void WaitForCompletion()
		{
			var idle = Stopwatch.StartNew();
			while (!_done)
			{
				var request = _request;
				if (request.isDone)
				{
					OnRequestDone(request);
					idle.Restart();
					continue;
				}

				if (request.downloadedBytes != _lastDownloadedBytes)
				{
					_lastDownloadedBytes = request.downloadedBytes;
					idle.Restart();
				}
				else if (TimeoutSeconds > 0 && idle.Elapsed.TotalSeconds >= TimeoutSeconds)
				{
					_timedOut = true;
					request.Abort();
				}
				Thread.Sleep(WaitSleepMs);
			}
		}

		// No-progress watchdog, ticked by the ResourceManager once per frame.
		public void Update(float unscaledDeltaTime)
		{
			if (_done || _request == null || _request.isDone)
				return;

			if (_request.downloadedBytes != _lastDownloadedBytes)
			{
				_lastDownloadedBytes = _request.downloadedBytes;
				_idleSeconds = 0f;
				return;
			}

			_idleSeconds += unscaledDeltaTime;
			if (_idleSeconds >= TimeoutSeconds)
			{
				_timedOut = true;
				_request.Abort();
			}
		}

		private void Send()
		{
			var request = UnityWebRequest.Get(_url);
			if (_downloadToPath != null)
				request.downloadHandler = new DownloadHandlerFile(_downloadToPath) { removeFileOnAbort = true };
			if (_options != null && _options.RedirectLimit >= 0 && _options.RedirectLimit < 129)
				request.redirectLimit = _options.RedirectLimit;
			if (_rm.CertificateHandlerInstance != null)
			{
				request.certificateHandler = _rm.CertificateHandlerInstance;
				request.disposeCertificateHandlerOnDispose = false;
			}
			_rm.WebRequestOverride?.Invoke(request);

			_request = request;
			_idleSeconds = 0f;
			_lastDownloadedBytes = 0;
			_timedOut = false;
			// Ignore the callback of a request already handled by WaitForCompletion or replaced by a retry.
			request.SendWebRequest().completed += _ =>
			{
				if (_request == request && !_done)
					OnRequestDone(request);
			};
		}

		private void OnRequestDone(UnityWebRequest request)
		{
			if (request.result == UnityWebRequest.Result.Success)
			{
				var bytes = _downloadToPath == null ? request.downloadHandler.data : null;
				_finalBytes = (long)request.downloadedBytes;
				request.Dispose();
				Finish(bytes, null);
				return;
			}

			var error = _timedOut
				? $"no progress for {TimeoutSeconds}s (timeout)"
				: $"{request.error} (HTTP {request.responseCode})";
			// 4xx (missing file, forbidden...) won't fix itself; 408 timeout and 429 rate limit can.
			var code = request.responseCode;
			var clientError = code >= 400 && code < 500 && code != 408 && code != 429;
			var canRetry = _timedOut || (!clientError && new UnityWebRequestResult(request).ShouldRetryDownloadError());
			request.Dispose();

			if (canRetry && _options != null && _retries < _options.RetryCount)
			{
				_retries++;
				Debug.LogWarning($"[AddressablesEncryption] Retry {_retries}/{_options.RetryCount} downloading '{_url}': {error}");
				Send();
				return;
			}

			Finish(null, new IOException($"[AddressablesEncryption] Failed to download encrypted bundle '{_url}': {error}"));
		}

		private void Finish(byte[] bytes, Exception error)
		{
			_done = true;
			if (error != null)
				_finalBytes = 0;
			_request = null;
			if (TimeoutSeconds > 0)
				_rm.RemoveUpdateReciever(this);
			_onDone(bytes, error);
		}
	}
}
