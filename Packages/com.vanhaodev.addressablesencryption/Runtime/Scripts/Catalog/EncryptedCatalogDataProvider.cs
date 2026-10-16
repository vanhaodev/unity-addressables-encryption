using System;
using System.ComponentModel;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Scripting;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Loads the bytes of an encrypted binary catalog, decrypts them with the current key and hands the plaintext
	/// to Addressables' own binary catalog parser. Catalog files without the AAEN header (the remote catalog and
	/// its cached copy, which stay plain in this version) pass straight through.
	/// </summary>
	// Created by type name from settings.json: keep it from IL2CPP stripping and exclude it from renaming obfuscators.
	[Preserve]
	[System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
	[DisplayName("Encrypted Catalog Data Provider")]
	public class EncryptedCatalogDataProvider : BinaryDataProvider
	{
		private readonly ResourceManager _resourceManager;

		public EncryptedCatalogDataProvider(ResourceManager resourceManager)
		{
			_resourceManager = resourceManager;
		}

		public override object Convert(Type type, byte[] data)
		{
			var plain = EncryptedBundleFormat.IsEncrypted(data) ? AddressablesEncryption.DecryptCatalog(data) : data;
			return FindStockParser().Convert(type, plain);
		}

		/// <summary>Addressables registers its binary catalog parser at init; it is internal, so look it up by id.</summary>
		private BinaryDataProvider FindStockParser()
		{
			foreach (var provider in _resourceManager.ResourceProviders)
			{
				if (provider.ProviderId == BinaryContentCatalogData.kBinaryAssetProviderId && provider is BinaryDataProvider parser)
					return parser;
			}
			throw new InvalidOperationException("[AddressablesEncryption] Addressables' binary catalog parser is not registered. Is the catalog loaded before Addressables initialized?");
		}
	}
}
