using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Pre-build checks for encrypted groups. Runs before any bundle is written,
	/// so a failed check never leaves plain bundles of an encrypted group on disk.
	/// </summary>
	internal static class EncryptedBundleBuildValidator
	{
		/// <summary>
		/// Streaming AudioClips (typical for BGM) are read gradually from the bundle file on disk.
		/// Encrypted bundles only exist decrypted in memory, so streaming is not supported: fail the build.
		/// </summary>
		public static void ValidateNoStreamingAudio(AddressableAssetSettings settings, ICollection<AddressableAssetGroup> encryptedGroups, BuildTarget target)
		{
			var platform = BuildPipeline.GetBuildTargetGroup(target).ToString();
			var offenders = new List<string>();

			foreach (var group in encryptedGroups)
			{
				foreach (var path in CollectBundledAssetPaths(settings, group, encryptedGroups))
				{
					if (AssetImporter.GetAtPath(path) is AudioImporter audio && IsStreaming(audio, platform))
						offenders.Add($"  - {path} (group '{group.Name}')");
				}
			}

			if (offenders.Count > 0)
				throw new InvalidOperationException(
					"[AddressablesEncryption] Streaming AudioClips are not supported in encrypted groups. " +
					"Move them to a group without the Encryption schema, or set Load Type to 'Compressed In Memory' / 'Decompress On Load':\n" +
					string.Join("\n", offenders.Distinct()));
		}

		private static bool IsStreaming(AudioImporter audio, string platform)
		{
			var sampleSettings = audio.ContainsSampleSettingsOverride(platform)
				? audio.GetOverrideSampleSettings(platform)
				: audio.defaultSampleSettings;
			return sampleSettings.loadType == AudioClipLoadType.Streaming;
		}

		/// <summary>
		/// Explicit entries (folders expanded) plus implicit dependencies that end up in this group's bundles.
		/// A dependency that is an entry of a non-encrypted group lives in that group's bundle, so it is skipped.
		/// </summary>
		private static IEnumerable<string> CollectBundledAssetPaths(AddressableAssetSettings settings, AddressableAssetGroup group, ICollection<AddressableAssetGroup> encryptedGroups)
		{
			var entries = new List<AddressableAssetEntry>();
			foreach (var entry in group.entries)
				entry.GatherAllAssets(entries, true, true, false);

			var roots = entries.Select(e => e.AssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
			foreach (var path in AssetDatabase.GetDependencies(roots, true))
			{
				var owner = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path), true);
				if (owner != null && owner.parentGroup != null && !encryptedGroups.Contains(owner.parentGroup))
					continue;
				yield return path;
			}
		}
	}
}
