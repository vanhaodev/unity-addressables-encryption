using System;
using UnityEditor;
using UnityEngine;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Editor-only build key. Never included in the player (Editor assembly type).<br/>
	/// The env var <see cref="KeyEnvVar"/> overrides the asset value (CI, keep the key out of git).<br/>
	/// The runtime must call AddressablesEncryption.SetKey with the same secret / raw key.
	/// </summary>
	public class AddressablesEncryptionSettings : ScriptableObject
	{
		public enum KeyFormat
		{
			/// <summary>Any string; matches AddressablesEncryption.SetKey(string).</summary>
			Secret,
			/// <summary>Base64 of a raw 16/24/32-byte AES key; matches AddressablesEncryption.SetKey(byte[]).</summary>
			RawKeyBase64
		}

		public const string KeyEnvVar = "ADDRESSABLES_ENCRYPTION_KEY";

		[SerializeField] private KeyFormat m_Format = KeyFormat.Secret;

		[Tooltip("Build key. Leave empty and set the " + KeyEnvVar + " environment variable to keep it out of version control.")]
		[SerializeField] private string m_Key = "";

		[Tooltip("Also encrypt the local catalog (hides asset addresses). The game must then call AddressablesEncryption.SetKey BEFORE Addressables initializes.")]
		[SerializeField] private bool m_EncryptCatalog;

		/// <summary>Key as typed in the asset (build-time only, read by tests and tooling).</summary>
		internal string Key => m_Key;
		internal KeyFormat Format => m_Format;

		/// <summary>Encrypt the local catalog file with the build key (off by default). Settable for tests and tooling.</summary>
		public bool EncryptCatalog
		{
			get => m_EncryptCatalog;
			set
			{
				if (m_EncryptCatalog == value)
					return;
				m_EncryptCatalog = value;
				EditorUtility.SetDirty(this);
			}
		}

		/// <summary>True when a settings asset exists and asks for catalog encryption.</summary>
		public static bool IsCatalogEncryptionEnabled()
		{
			var settings = Find();
			return settings != null && settings.m_EncryptCatalog;
		}

		/// <summary>Find the settings asset in the project, or null.</summary>
		public static AddressablesEncryptionSettings Find()
		{
			var guids = AssetDatabase.FindAssets("t:" + nameof(AddressablesEncryptionSettings));
			if (guids.Length > 1)
				Debug.LogWarning($"[AddressablesEncryption] {guids.Length} settings assets found, using the first: {AssetDatabase.GUIDToAssetPath(guids[0])}");
			return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<AddressablesEncryptionSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
		}

		/// <summary>Resolve the AES key used to encrypt bundles. Throws with a clear message if missing or invalid.</summary>
		public static byte[] ResolveBuildKey()
		{
			var settings = Find();
			var format = settings != null ? settings.m_Format : KeyFormat.Secret;
			var key = Environment.GetEnvironmentVariable(KeyEnvVar);
			if (string.IsNullOrEmpty(key))
				key = settings != null ? settings.m_Key : null;

			if (string.IsNullOrEmpty(key))
				throw new InvalidOperationException(
					$"[AddressablesEncryption] No build key. Set it in the Addressables Encryption Settings asset (Tools > Addressables Encryption > Setup) or the {KeyEnvVar} environment variable.");

			if (format == KeyFormat.Secret)
				return AddressablesEncryption.DeriveKey(key);

			byte[] raw;
			try
			{
				raw = Convert.FromBase64String(key);
			}
			catch (FormatException e)
			{
				throw new InvalidOperationException("[AddressablesEncryption] Build key is not valid Base64 (format is RawKeyBase64).", e);
			}
			AddressablesEncryption.ValidateRawKey(raw);
			return raw;
		}
	}
}
