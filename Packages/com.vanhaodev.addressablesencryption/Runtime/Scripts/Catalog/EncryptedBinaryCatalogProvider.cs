using System.ComponentModel;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.AddressableAssets.ResourceProviders;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Scripting;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Catalog provider written into settings.json when "Encrypt Catalog" is on. Same as Addressables' binary
	/// catalog provider, except that the catalog bytes go through <see cref="EncryptedCatalogDataProvider"/>.
	/// </summary>
	// Created by type name from settings.json: keep it from IL2CPP stripping and exclude it from renaming obfuscators.
	[Preserve]
	[System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
	[DisplayName("Encrypted Binary Catalog Provider")]
	public class EncryptedBinaryCatalogProvider : BinaryCatalogProvider
	{
		public EncryptedBinaryCatalogProvider(ResourceManager resourceManager) : base(resourceManager) { }

		protected override IResourceLocation CreateInnerCatalogLocation(string idToLoad, ProviderLoadRequestOptions opts)
		{
			var location = new ResourceLocationBase(idToLoad, idToLoad, typeof(EncryptedCatalogDataProvider).FullName, typeof(BinaryContentCatalogData));
			location.Data = opts;
			return location;
		}
	}
}
