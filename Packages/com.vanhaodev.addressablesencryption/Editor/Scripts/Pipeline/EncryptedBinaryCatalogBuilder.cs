using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.CatalogBuilders;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.AddressableAssets.ResourceProviders;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Catalog builder used when "Encrypt Catalog" is on: lets Addressables write its binary catalog, then
	/// encrypts the local catalog file in place and points its location at <see cref="EncryptedBinaryCatalogProvider"/>.
	/// The remote catalog (and its .hash files) stay plain in this version.<br/>
	/// Deliberately not a <c>BaseCatalogBuilder</c>: Addressables discovers those by reflection for its
	/// "Catalog Provider" dropdown, and this builder only works together with the encrypted build script.
	/// </summary>
	internal sealed class EncryptedBinaryCatalogBuilder : ICatalogBuilder
	{
		private readonly BinaryCatalogBuilder _inner = new BinaryCatalogBuilder();
		private readonly byte[] _key;

		public EncryptedBinaryCatalogBuilder(byte[] key)
		{
			_key = key;
		}

		public Type CatalogProviderType => typeof(EncryptedBinaryCatalogProvider);
		public string CatalogExtension => _inner.CatalogExtension;
		public bool SupportsLocalCatalogBundling => false;

		public ContentCatalogData GenerateCatalog(IBuildLogger logger, CatalogPathConfig catalogPaths, string catalogLocatorId,
			IList<ContentCatalogDataEntry> catalogDataEntries, List<ResourceLocationData> catalogLocations, HashSet<Type> providerTypes,
			FileRegistry registry, string buildResultHash, bool buildRemoteCatalog, int catalogRequestsTimeout, CatalogBundleConfig catalogBundleConfig = null)
		{
			var catalog = _inner.GenerateCatalog(logger, catalogPaths, catalogLocatorId, catalogDataEntries, catalogLocations, providerTypes,
				registry, buildResultHash, buildRemoteCatalog, catalogRequestsTimeout, catalogBundleConfig);

			var path = LocalCatalogPath(catalogPaths);
			File.WriteAllBytes(path, EncryptedBundleFormat.Encrypt(File.ReadAllBytes(path), _key));
			UseEncryptedProvider(catalogLocations);
			Debug.Log($"[AddressablesEncryption] Encrypted catalog '{Path.GetFileName(path)}'.");
			return catalog;
		}

		/// <summary>Same file Addressables wrote: Addressables.BuildPath/&lt;RuntimeCatalogFilename&gt;.bin.</summary>
		private string LocalCatalogPath(CatalogPathConfig catalogPaths)
		{
			var fileName = catalogPaths.RuntimeCatalogFilename;
			if (Path.GetExtension(fileName) != "." + CatalogExtension)
				fileName += "." + CatalogExtension;
			return Path.Combine(Addressables.BuildPath, fileName);
		}

		/// <summary>Replace the stock catalog location (provider id is read-only) with one that names our provider.</summary>
		private static void UseEncryptedProvider(List<ResourceLocationData> catalogLocations)
		{
			var stockProvider = typeof(BinaryCatalogProvider).FullName;
			for (int i = 0; i < catalogLocations.Count; i++)
			{
				var location = catalogLocations[i];
				if (location.Provider != stockProvider)
					continue;
				catalogLocations[i] = new ResourceLocationData(location.Keys, location.InternalId, typeof(EncryptedBinaryCatalogProvider),
					typeof(BinaryContentCatalogData), location.Dependencies) { Data = location.Data };
			}
		}
	}
}
