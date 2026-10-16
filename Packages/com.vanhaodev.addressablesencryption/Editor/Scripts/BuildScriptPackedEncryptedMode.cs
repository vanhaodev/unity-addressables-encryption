using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Default packed build + AES encryption of the bundles of groups with an enabled <see cref="EncryptedBundleGroupSchema"/>.<br/>
	/// Order: validate (key, streaming audio) → assign providers → stock build → encrypt bundle files in place.<br/>
	/// Set as the active player data builder via Tools > Addressables Encryption > Setup.
	/// </summary>
	[CreateAssetMenu(fileName = nameof(BuildScriptPackedEncryptedMode) + ".asset", menuName = "Addressables/Content Builders/Encrypted Build Script")]
	public class BuildScriptPackedEncryptedMode : BuildScriptPackedMode
	{
		public override string Name => "Encrypted Build Script (AssetBundles)";

		protected override TResult BuildDataImplementation<TResult>(AddressablesDataBuilderInput builderInput)
		{
			var settings = builderInput.AddressableSettings;
			var encryptedGroups = new HashSet<AddressableAssetGroup>(settings.groups.Where(EncryptedBundleGroupSchema.IsEncrypted));

			// Exceptions here abort before any bundle is written; BuildData reports them as the build error.
			byte[] key = null;
			if (encryptedGroups.Count > 0)
			{
				// Content update moves changed entries of static groups into a new group without the Encryption schema
				// (shipped as plaintext) and reverts unchanged bundles to the previous provider/file. Not handled yet.
				if (builderInput.PreviousContentState != null)
					throw new System.InvalidOperationException(
						"[AddressablesEncryption] Content update builds are not supported for encrypted groups yet. Make a full build instead.");

				key = AddressablesEncryptionSettings.ResolveBuildKey();
				EncryptedBundleBuildValidator.ValidateNoStreamingAudio(settings, encryptedGroups, builderInput.Target);
			}

			// Must happen before the catalog is generated: the catalog stores each bundle's provider.
			// Restored afterwards so a later build with another builder never pairs our provider with plain bundles.
			var originalProviders = AssignBundleProviders(settings, encryptedGroups);
			try
			{
				var result = base.BuildDataImplementation<TResult>(builderInput);

				if (key != null && result is AddressablesPlayerBuildResult playerResult && string.IsNullOrEmpty(playerResult.Error))
					EncryptBundles(playerResult, encryptedGroups, key);

				return result;
			}
			finally
			{
				foreach (var pair in originalProviders)
					pair.Key.AssetBundleProviderType = pair.Value;
			}
		}

		/// <summary>
		/// Point encrypted groups at <see cref="EncryptedAssetBundleProvider"/>. Returns the previous provider of each changed group.
		/// </summary>
		private static Dictionary<BundledAssetGroupSchema, SerializedType> AssignBundleProviders(AddressableAssetSettings settings, HashSet<AddressableAssetGroup> encryptedGroups)
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

		/// <summary>
		/// All-or-nothing: if any bundle cannot be encrypted, every plaintext bundle of an encrypted group is deleted,
		/// so a failed build never leaves unprotected files in the output.
		/// </summary>
		private static void EncryptBundles(AddressablesPlayerBuildResult result, HashSet<AddressableAssetGroup> encryptedGroups, byte[] key)
		{
			var paths = result.AssetBundleBuildResults
				.Where(b => encryptedGroups.Contains(b.SourceAssetGroup) && File.Exists(b.FilePath))
				.Select(b => b.FilePath)
				.Distinct()
				.ToList();
			try
			{
				for (int i = 0; i < paths.Count; i++)
				{
					EditorUtility.DisplayProgressBar("Addressables Encryption", Path.GetFileName(paths[i]), (float)i / paths.Count);

					var bytes = File.ReadAllBytes(paths[i]);
					if (EncryptedBundleFormat.IsEncrypted(bytes))
						continue; // reused output of a previous build, already encrypted
					File.WriteAllBytes(paths[i], EncryptedBundleFormat.Encrypt(bytes, key));
				}
			}
			catch
			{
				DeletePlaintextBundles(paths);
				throw;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			Debug.Log($"[AddressablesEncryption] Encrypted {paths.Count} bundle(s) from {encryptedGroups.Count} group(s).");
		}

		private static void DeletePlaintextBundles(List<string> paths)
		{
			foreach (var path in paths)
			{
				try
				{
					if (File.Exists(path) && !EncryptedBundleFormat.IsEncrypted(File.ReadAllBytes(path)))
						File.Delete(path);
				}
				catch (System.Exception e)
				{
					Debug.LogError($"[AddressablesEncryption] Could not delete plaintext bundle '{path}': {e.Message}");
				}
			}
		}
	}
}
