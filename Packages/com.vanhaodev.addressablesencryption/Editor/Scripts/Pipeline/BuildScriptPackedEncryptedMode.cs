using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Build.Pipeline;

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
			var contentUpdate = builderInput.PreviousContentState != null;

			// Content update: the group holding the changed assets was created without our schema; give it one
			// before deciding which groups are encrypted.
			if (contentUpdate)
				ContentUpdateEncryptionGuard.InheritEncryptionSchema(settings, builderInput.PreviousContentState);

			var encryptedGroups = new HashSet<AddressableAssetGroup>(settings.groups.Where(EncryptedBundleGroupSchema.IsEncrypted));

			// Exceptions here abort before any bundle is written; BuildData reports them as the build error.
			// The content update check runs even with no encrypted group left: that is exactly the case where a
			// previously encrypted bundle would be reverted into a plain catalog.
			if (contentUpdate)
				ContentUpdateEncryptionGuard.ValidateNoPlaintextShip(settings, builderInput.PreviousContentState);

			byte[] key = null;
			if (encryptedGroups.Count > 0)
			{
				key = AddressablesEncryptionSettings.ResolveBuildKey();
				EncryptedBundleBuildValidator.ValidateNoStreamingAudio(settings, encryptedGroups, builderInput.Target);
			}

			var originalProviders = EncryptedBundleProviderAssigner.Assign(settings, encryptedGroups);
			try
			{
				var result = base.BuildDataImplementation<TResult>(builderInput);
				var playerResult = result as AddressablesPlayerBuildResult;
				var succeeded = playerResult != null && string.IsNullOrEmpty(playerResult.Error);

				if (key != null && succeeded)
					EncryptedBundleFileEncryptor.EncryptBundles(playerResult, encryptedGroups, key);
				else if (encryptedGroups.Count == 0 && succeeded)
					Debug.LogWarning("[AddressablesEncryption] Built with the encrypted build script, but no group has the Encryption schema enabled: nothing was encrypted.");

				if (succeeded && AddressablesEncryptionSettings.IsCatalogEncryptionEnabled())
					EncryptedCatalogPostProcessor.Apply(Addressables.BuildPath, key ?? AddressablesEncryptionSettings.ResolveBuildKey());

				return result;
			}
			finally
			{
				EncryptedBundleProviderAssigner.Restore(originalProviders);
			}
		}

		protected override string ConstructAssetBundleName(AddressableAssetGroup assetGroup, BundledAssetGroupSchema schema, BundleDetails info, string assetBundleName)
		{
			var stockName = base.ConstructAssetBundleName(assetGroup, schema, info, assetBundleName);
			return EncryptedBundleFileNaming.Apply(assetGroup, schema, info, stockName);
		}
	}
}
