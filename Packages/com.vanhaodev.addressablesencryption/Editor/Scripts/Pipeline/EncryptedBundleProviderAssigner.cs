using System.Collections.Generic;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Points encrypted groups at <see cref="EncryptedAssetBundleProvider"/> for the duration of one build.
	/// The catalog stores each bundle's provider, so this must run before the catalog is generated and be
	/// restored afterwards: a later build with another builder must never pair our provider with plain bundles.
	/// </summary>
	internal static class EncryptedBundleProviderAssigner
	{
		/// <summary>Assign providers. Returns the previous provider of every changed group, for <see cref="Restore"/>.</summary>
		public static Dictionary<BundledAssetGroupSchema, SerializedType> Assign(AddressableAssetSettings settings, HashSet<AddressableAssetGroup> encryptedGroups)
		{
			var original = new Dictionary<BundledAssetGroupSchema, SerializedType>();
			foreach (var group in settings.groups)
			{
				var bundled = group != null ? group.GetSchema<BundledAssetGroupSchema>() : null;
				if (bundled == null)
					continue;

				var current = bundled.AssetBundleProviderType.Value;
				if (encryptedGroups.Contains(group))
				{
					if (current == typeof(EncryptedAssetBundleProvider))
						continue;
					original[bundled] = bundled.AssetBundleProviderType;
					bundled.AssetBundleProviderType = new SerializedType { Value = typeof(EncryptedAssetBundleProvider) };
				}
				else if (current == typeof(EncryptedAssetBundleProvider))
				{
					// Left over (e.g. set by hand): this group is built plain, so it needs the project's normal provider.
					var fallback = settings.AssetBundleProviderType.Value;
					if (fallback == null || fallback == typeof(EncryptedAssetBundleProvider))
						fallback = typeof(AssetBundleProvider);
					bundled.AssetBundleProviderType = new SerializedType { Value = fallback };
				}
			}
			return original;
		}

		/// <summary>Put back the providers recorded by <see cref="Assign"/>.</summary>
		public static void Restore(Dictionary<BundledAssetGroupSchema, SerializedType> original)
		{
			foreach (var pair in original)
				pair.Key.AssetBundleProviderType = pair.Value;
		}
	}
}
