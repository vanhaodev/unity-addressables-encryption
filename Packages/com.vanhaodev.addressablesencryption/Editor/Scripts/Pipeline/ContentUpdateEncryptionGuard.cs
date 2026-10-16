using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Keeps "Update a Previous Build" from shipping formerly encrypted assets as plaintext.<br/>
	/// Addressables reverts unchanged assets of static groups to their previous bundle file and moves changed ones
	/// into a new group that has no Encryption schema. The content state records each asset's previous group, which
	/// is enough to (1) inherit the schema onto that new group and (2) refuse builds where a previous bundle file
	/// and its group's current encryption setting disagree.
	/// </summary>
	internal static class ContentUpdateEncryptionGuard
	{
		/// <summary>
		/// Add the Encryption schema to every group that has none but holds an entry that came from an encrypted
		/// group (typically the group made by "Check for Content Update Restrictions"). Returns the changed groups.
		/// </summary>
		public static List<AddressableAssetGroup> InheritEncryptionSchema(AddressableAssetSettings settings, AddressablesContentState previous)
		{
			var previousGroupOfAsset = PreviousGroupOfAsset(previous);
			var changed = new List<AddressableAssetGroup>();
			foreach (var group in settings.groups)
			{
				if (group == null || EncryptedBundleGroupSchema.IsEncrypted(group))
					continue;

				var source = FindEncryptedSourceGroup(settings, group, previousGroupOfAsset);
				if (source == null)
					continue;

				var schema = group.GetSchema<EncryptedBundleGroupSchema>() ?? group.AddSchema<EncryptedBundleGroupSchema>();
				schema.IsEnabled = true;
				schema.HashBundleFileNames = source.GetSchema<EncryptedBundleGroupSchema>().HashBundleFileNames;
				Debug.Log($"[AddressablesEncryption] Group '{group.Name}' inherits the Encryption schema from '{source.Name}' (content update).");
				changed.Add(group);
			}
			return changed;
		}

		/// <summary>
		/// Throws when the update would pair a previous bundle file with the wrong provider, or when the shared
		/// bundles (built-in shaders, MonoScripts) belong to an encrypted static group, which Addressables cannot revert.
		/// </summary>
		public static void ValidateNoPlaintextShip(AddressableAssetSettings settings, AddressablesContentState previous)
		{
			var shared = settings.GetSharedBundleGroup();
			if (EncryptedBundleGroupSchema.IsEncrypted(shared) && IsStatic(shared))
				throw new System.InvalidOperationException(
					$"[AddressablesEncryption] Group '{shared.Name}' holds the shared bundles (built-in shaders, MonoScripts) and is both encrypted and marked 'Prevent Updates'. " +
					"Addressables cannot revert shared bundles for an encrypted group during a content update. Turn 'Prevent Updates' off for that group, remove its Encryption schema, or make a full build.");

			var offenders = new HashSet<string>();
			foreach (var info in previous.cachedInfos ?? new CachedAssetState[0])
			{
				var group = FindGroupByGuid(settings, info.groupGuid);
				if (group == null || !IsStatic(group) || string.IsNullOrEmpty(info.bundleFileId))
					continue;
				var entry = settings.FindAssetEntry(info.asset.guid.ToString());
				if (entry == null || entry.parentGroup != group)
					continue; // moved or removed: nothing of it is reverted

				var previousBundle = PreviousBundlePath(group, info.bundleFileId);
				if (previousBundle == null || !File.Exists(previousBundle))
					continue; // Addressables warns about the missing file itself
				if (EncryptedBundleFormat.IsEncryptedFile(previousBundle) != EncryptedBundleGroupSchema.IsEncrypted(group))
					offenders.Add(group.Name);
			}

			if (offenders.Count > 0)
				throw new System.InvalidOperationException(
					"[AddressablesEncryption] The Encryption setting of these 'Prevent Updates' groups changed since the previous build: " + string.Join(", ", offenders) +
					". Their unchanged assets would be reverted to a previous bundle file that the new catalog cannot load. Restore the setting, or make a full build.");
		}

		private static Dictionary<string, string> PreviousGroupOfAsset(AddressablesContentState previous)
		{
			var map = new Dictionary<string, string>();
			foreach (var info in previous.cachedInfos ?? new CachedAssetState[0])
				map[info.asset.guid.ToString()] = info.groupGuid;
			return map;
		}

		private static AddressableAssetGroup FindEncryptedSourceGroup(AddressableAssetSettings settings, AddressableAssetGroup group, Dictionary<string, string> previousGroupOfAsset)
		{
			var entries = new List<AddressableAssetEntry>();
			group.GatherAllAssets(entries, true, true, false);
			foreach (var entry in entries)
			{
				if (!previousGroupOfAsset.TryGetValue(entry.guid, out var previousGroupGuid) || previousGroupGuid == group.Guid)
					continue;
				var source = FindGroupByGuid(settings, previousGroupGuid);
				if (source != null && EncryptedBundleGroupSchema.IsEncrypted(source))
					return source;
			}
			return null;
		}

		private static AddressableAssetGroup FindGroupByGuid(AddressableAssetSettings settings, string guid)
		{
			foreach (var group in settings.groups)
			{
				if (group != null && group.Guid == guid)
					return group;
			}
			return null;
		}

		private static bool IsStatic(AddressableAssetGroup group)
		{
			var schema = group.GetSchema<ContentUpdateGroupSchema>();
			return schema != null && schema.IsEnabled && schema.StaticContent;
		}

		/// <summary>Same mapping Addressables uses to find a previous bundle on disk: load path → build path.</summary>
		private static string PreviousBundlePath(AddressableAssetGroup group, string bundleFileId)
		{
			var bundled = group.GetSchema<BundledAssetGroupSchema>();
			if (bundled == null)
				return null;
			var loadPath = bundled.LoadPath.GetValue(group.Settings);
			var buildPath = bundled.BuildPath.GetValue(group.Settings);
			var replaceBackslashes = loadPath.Contains("/") && !ResourceManagerConfig.ShouldPathUseWebRequest(loadPath);
			var id = replaceBackslashes ? bundleFileId.Replace('\\', '/') : bundleFileId;
			return id.Replace(loadPath, buildPath);
		}
	}
}
