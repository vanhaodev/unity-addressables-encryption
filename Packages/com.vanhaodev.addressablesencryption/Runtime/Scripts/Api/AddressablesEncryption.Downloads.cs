using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Download size and cache control for remote encrypted bundles.<br/>
	/// Addressables.GetDownloadSizeAsync only knows Unity's native cache, so it always counts encrypted bundles
	/// as "to download". Use <see cref="GetDownloadSizeAsync(object)"/> instead: same result for plain bundles,
	/// and encrypted bundles count as 0 once they are in the encrypted cache.
	/// </summary>
	public static partial class AddressablesEncryption
	{
		/// <summary>
		/// Bytes still to download for key (address, label, AssetReference or a list of them, union).
		/// Covers plain and encrypted bundles. Size of an encrypted bundle is its pre-encryption size (±32 bytes).
		/// </summary>
		public static async Task<long> GetDownloadSizeAsync(object key)
		{
			var handle = key is IEnumerable keys && !(key is string)
				? Addressables.LoadResourceLocationsAsync(keys, Addressables.MergeMode.Union)
				: Addressables.LoadResourceLocationsAsync(key);
			try
			{
				var locations = await handle.Task;
				if (locations == null)
					throw handle.OperationException ?? new InvalidOperationException($"No locations for key '{key}'.");

				var visited = new HashSet<string>();
				long total = 0;
				foreach (var location in locations)
					total += SumDependencies(location, visited);
				return total;
			}
			finally
			{
				if (handle.IsValid())
					handle.Release();
			}
		}

		/// <summary>
		/// Delete every cached encrypted bundle. Addressables.ClearDependencyCacheAsync only clears Unity's
		/// native cache (plain bundles); call both to clear everything.
		/// </summary>
		public static void ClearCache()
		{
			EncryptedBundleCache.Clear();
		}

		private static long SumDependencies(IResourceLocation location, HashSet<string> visited)
		{
			if (!location.HasDependencies)
				return 0;

			long total = 0;
			var rm = Addressables.ResourceManager;
			foreach (var dep in location.Dependencies)
			{
				var id = rm.TransformInternalId(dep);
				if (!visited.Add(dep.ProviderId + "|" + id))
					continue;

				if (dep.Data is AssetBundleRequestOptions options)
				{
					if (IsEncrypted(dep))
					{
						if (ResourceManagerConfig.IsPathRemote(id) && !EncryptedBundleCache.IsCached(options.BundleName, options.Hash))
							total += options.BundleSize;
					}
					else
					{
						total += options.ComputeSize(dep, rm);
					}
				}

				total += SumDependencies(dep, visited);
			}
			return total;
		}

		// ProviderId is the type name, plus "_<group guid>" when the group forces a unique provider.
		private static bool IsEncrypted(IResourceLocation location)
		{
			return location.ProviderId != null &&
				location.ProviderId.StartsWith(typeof(EncryptedAssetBundleProvider).FullName, StringComparison.Ordinal);
		}
	}
}
