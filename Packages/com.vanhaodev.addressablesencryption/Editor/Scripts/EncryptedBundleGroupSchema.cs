using System.Collections.Generic;
using System.ComponentModel;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Add this schema to a group to AES-encrypt its bundles (Add Schema > Encryption in the group inspector).
	/// Groups without it, or with it disabled, are built as usual.<br/>
	/// Only applied by the "Encrypted Build Script (AssetBundles)" data builder.
	/// </summary>
	[DisplayName("Encryption")]
	public class EncryptedBundleGroupSchema : AddressableAssetGroupSchema
	{
		private const string Help =
			"Bundles of this group are AES-encrypted when built with \"Encrypted Build Script (AssetBundles)\".\n" +
			"Call AddressablesEncryption.SetKey before loading. Streaming AudioClips are not allowed in this group.";

		public override void OnGUI()
		{
			EditorGUILayout.HelpBox(Help, MessageType.Info);
		}

		public override void OnGUIMultiple(List<AddressableAssetGroupSchema> otherSchemas)
		{
			EditorGUILayout.HelpBox(Help, MessageType.Info);
		}

		/// <summary>True if the group has this schema and it is enabled.</summary>
		public static bool IsEncrypted(AddressableAssetGroup group)
		{
			var schema = group != null ? group.GetSchema<EncryptedBundleGroupSchema>() : null;
			return schema != null && schema.IsEnabled;
		}
	}
}
