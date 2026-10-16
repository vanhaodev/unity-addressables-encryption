using System;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.AddressableAssets.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// "Encrypt Catalog", applied after the stock build wrote its output:<br/>
	/// 1. encrypts the local binary catalog file in place (AAEN header, same format as bundles);<br/>
	/// 2. points that catalog's location in settings.json at <see cref="EncryptedBinaryCatalogProvider"/>;<br/>
	/// 3. registers <see cref="EncryptedCatalogInitialization"/>, which adds our providers before any catalog loads.<br/>
	/// Addressables offers no hook between writing the catalog and writing settings.json (its catalog builder hook
	/// is not reached through BuildScriptPackedMode), so both files are patched here. The remote catalog stays plain.
	/// </summary>
	internal static class EncryptedCatalogPostProcessor
	{
		public const string SettingsFileName = "settings.json";

		public static void Apply(string buildPath, byte[] key)
		{
			var settingsPath = Path.Combine(buildPath, SettingsFileName);
			var runtimeData = JsonUtility.FromJson<ResourceManagerRuntimeData>(File.ReadAllText(settingsPath));

			var stockProvider = typeof(BinaryCatalogProvider).FullName;
			var encryptedCount = 0;
			var locations = runtimeData.CatalogLocations;
			for (int i = 0; i < locations.Count; i++)
			{
				var location = locations[i];
				if (location.Provider != stockProvider)
					continue;

				EncryptCatalogFile(Path.Combine(buildPath, Path.GetFileName(location.InternalId)), key);
				var replacement = new ResourceLocationData(location.Keys, location.InternalId, typeof(EncryptedBinaryCatalogProvider),
					typeof(BinaryContentCatalogData), location.Dependencies);
				if (location.Data != null)
					replacement.Data = location.Data; // the setter does not accept null
				locations[i] = replacement;
				encryptedCount++;
			}

			if (encryptedCount == 0)
				throw new InvalidOperationException(
					"[AddressablesEncryption] 'Encrypt Catalog' needs the Binary catalog provider (Addressables settings > Catalog Provider), or turn 'Encrypt Catalog' off.");

			AddInitializationObject(runtimeData);
			File.WriteAllText(settingsPath, JsonUtility.ToJson(runtimeData));
		}

		private static void EncryptCatalogFile(string path, byte[] key)
		{
			if (!File.Exists(path))
				throw new FileNotFoundException("[AddressablesEncryption] Built catalog not found: " + path);
			if (EncryptedBundleFormat.IsEncryptedFile(path))
				return;
			File.WriteAllBytes(path, EncryptedBundleFormat.Encrypt(File.ReadAllBytes(path), key));
			Debug.Log($"[AddressablesEncryption] Encrypted catalog '{Path.GetFileName(path)}'.");
		}

		private static void AddInitializationObject(ResourceManagerRuntimeData runtimeData)
		{
			var init = ObjectInitializationData.CreateSerializedInitializationData<EncryptedCatalogInitialization>();
			foreach (var existing in runtimeData.InitializationObjects)
			{
				if (existing.Id == init.Id)
					return;
			}
			runtimeData.InitializationObjects.Add(init);
		}
	}
}
