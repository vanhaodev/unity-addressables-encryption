using System.Collections.Generic;
using System.ComponentModel;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

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
			"Call AddressablesEncryption.SetKey before loading.\n" +
			"Remote bundles are cached (still encrypted) when 'Use Asset Bundle Cache' is on; CRC options are ignored.";

		private static readonly GUIContent HashNamesLabel = new GUIContent("Hash Bundle File Names",
			"Name bundle files by content hash only (no group/bundle name on disk or CDN). Applied at build time; the group's Bundle Naming setting is not changed.");

		[SerializeField] private bool m_HashBundleFileNames = true;

		/// <summary>Build bundle files named by content hash only, hiding group names on disk and CDN.</summary>
		public bool HashBundleFileNames
		{
			get => m_HashBundleFileNames;
			set
			{
				if (m_HashBundleFileNames == value)
					return;
				m_HashBundleFileNames = value;
				SetDirty(true);
			}
		}

		public override void OnGUI()
		{
			EditorGUILayout.HelpBox(Help, MessageType.Info);
			var value = EditorGUILayout.Toggle(HashNamesLabel, m_HashBundleFileNames);
			if (value != m_HashBundleFileNames)
				SetHashNames(this, value);
		}

		public override void OnGUIMultiple(List<AddressableAssetGroupSchema> otherSchemas)
		{
			EditorGUILayout.HelpBox(Help, MessageType.Info);
			EditorGUI.showMixedValue = otherSchemas.Exists(s => s is EncryptedBundleGroupSchema e && e.m_HashBundleFileNames != m_HashBundleFileNames);
			EditorGUI.BeginChangeCheck();
			var value = EditorGUILayout.Toggle(HashNamesLabel, m_HashBundleFileNames);
			EditorGUI.showMixedValue = false;
			if (!EditorGUI.EndChangeCheck())
				return;

			SetHashNames(this, value);
			foreach (var s in otherSchemas)
			{
				if (s is EncryptedBundleGroupSchema other)
					SetHashNames(other, value);
			}
		}

		// Through SerializedObject so the change is undoable and saved like any inspector edit.
		private static void SetHashNames(EncryptedBundleGroupSchema schema, bool value)
		{
			var so = new SerializedObject(schema);
			so.FindProperty(nameof(m_HashBundleFileNames)).boolValue = value;
			so.ApplyModifiedProperties();
			schema.SetDirty(true);
		}

		/// <summary>True if the group has this schema and it is enabled.</summary>
		public static bool IsEncrypted(AddressableAssetGroup group)
		{
			var schema = group != null ? group.GetSchema<EncryptedBundleGroupSchema>() : null;
			return schema != null && schema.IsEnabled;
		}
	}
}
