using System;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// Minimal HTTP server for the remote test group: serves ServerData/&lt;BuildTarget&gt;/ on
	/// http://localhost:8765/&lt;BuildTarget&gt;/ and counts requests (to tell downloads from cache hits).
	/// </summary>
	internal sealed class LocalBundleServer : IDisposable
	{
		public const string PortText = "8765";

		private readonly HttpListener _listener = new HttpListener();
		private readonly string _root;
		private readonly Thread _thread;
		private int _requestCount;

		public int RequestCount => Volatile.Read(ref _requestCount);

		public LocalBundleServer()
		{
			_root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ServerData"));
			if (!Directory.Exists(_root) || Directory.GetFiles(_root, "*.bundle", SearchOption.AllDirectories).Length == 0)
				throw new InvalidOperationException($"No remote bundles in '{_root}'. The PlayMode prebuild step did not run or failed.");

			_listener.Prefixes.Add("http://localhost:" + PortText + "/");
			_listener.Start();
			_thread = new Thread(Serve) { IsBackground = true };
			_thread.Start();
		}

		public void ResetCount() => Interlocked.Exchange(ref _requestCount, 0);

		private void Serve()
		{
			while (_listener.IsListening)
			{
				HttpListenerContext context;
				try
				{
					context = _listener.GetContext();
				}
				catch (Exception)
				{
					return; // listener stopped
				}

				Interlocked.Increment(ref _requestCount);
				var relative = Uri.UnescapeDataString(context.Request.Url.AbsolutePath.TrimStart('/'));
				var path = Path.GetFullPath(Path.Combine(_root, relative));
				var response = context.Response;
				try
				{
					if (path.StartsWith(_root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
					{
						var bytes = File.ReadAllBytes(path);
						response.ContentLength64 = bytes.Length;
						response.OutputStream.Write(bytes, 0, bytes.Length);
					}
					else
					{
						response.StatusCode = 404;
					}
				}
				finally
				{
					response.Close();
				}
			}
		}

		public void Dispose()
		{
			_listener.Stop();
			_listener.Close();
		}
	}
}
