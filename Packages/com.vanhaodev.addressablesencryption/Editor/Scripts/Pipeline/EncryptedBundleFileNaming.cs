using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine.Build.Pipeline;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// File names for bundles of encrypted groups with <see cref="EncryptedBundleGroupSchema.HashBundleFileNames"/>:
	/// no group or bundle name on disk or on the CDN.
	/// AppendHash: content hash only. NoHash: stable hash of the name, keeping the "_hash" suffix the stock pipeline
	/// strips later. OnlyHash / FileNameHash are already anonymous.
	/// </summary>
	internal static class EncryptedBundleFileNaming
	{
		/// <param name="stockName">Name produced by the stock <c>ConstructAssetBundleName</c>.</param>
		public static string Apply(AddressableAssetGroup group, BundledAssetGroupSchema schema, BundleDetails info, string stockName)
		{
			var encryption = group != null ? group.GetSchema<EncryptedBundleGroupSchema>() : null;
			if (encryption == null || !encryption.IsEnabled || !encryption.HashBundleFileNames)
				return stockName;

			var hash = info.Hash.ToString();
			switch (schema.BundleNaming)
			{
				case BundledAssetGroupSchema.BundleNamingStyle.AppendHash:
					return hash + ".bundle";
				case BundledAssetGroupSchema.BundleNamingStyle.NoHash:
					var plainName = stockName.Replace("_" + hash + ".bundle", ".bundle");
					return NameHash(plainName) + "_" + hash + ".bundle";
				default:
					return stockName;
			}
		}

		/// <summary>First 16 bytes of SHA-256 of the name, hex. Stable across builds.</summary>
		private static string NameHash(string value)
		{
			using var sha = System.Security.Cryptography.SHA256.Create();
			var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
			return System.BitConverter.ToString(bytes, 0, 16).Replace("-", "").ToLowerInvariant();
		}
	}
}
