using System;
using System.ComponentModel;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Scripting;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// AssetBundle provider for groups encrypted by the encrypted build script.
	/// Assigned automatically to encrypted groups at build time; do not set it by hand.
	/// </summary>
	[Preserve] // created by type name from the catalog, keep it from IL2CPP stripping
	[DisplayName("Encrypted AssetBundle Provider")]
	public class EncryptedAssetBundleProvider : ResourceProviderBase
	{
		[Preserve]
		public EncryptedAssetBundleProvider() { }

		public override void Provide(ProvideHandle providerInterface)
		{
			if (providerInterface.Location is { } location && location.GetType().Name == "DownloadOnlyLocation")
			{
				// DownloadDependenciesAsync: there is no disk cache for encrypted bundles (v1), so nothing to pre-download.
				providerInterface.Complete<EncryptedAssetBundleResource>(null, true, null);
				return;
			}

			new EncryptedAssetBundleResource().Start(providerInterface);
		}

		public override Type GetDefaultType(IResourceLocation location)
		{
			return typeof(IAssetBundleResource);
		}

		public override void Release(IResourceLocation location, object asset)
		{
			if (location == null)
				throw new ArgumentNullException(nameof(location));

			(asset as EncryptedAssetBundleResource)?.Unload();
		}
	}
}
