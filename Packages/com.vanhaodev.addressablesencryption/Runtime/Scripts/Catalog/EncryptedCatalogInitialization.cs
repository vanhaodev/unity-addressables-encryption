using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.Scripting;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// Addressables initialization object, written into settings.json by the encrypted build script when
	/// "Encrypt Catalog" is on. Addressables runs it before loading any catalog, which is when the two catalog
	/// providers must already be registered.
	/// </summary>
	// Created by type name from settings.json: keep it from IL2CPP stripping and exclude it from renaming obfuscators.
	[Preserve]
	[System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
	public class EncryptedCatalogInitialization : IInitializableObject
	{
		[Preserve]
		public EncryptedCatalogInitialization() { }

		public bool Initialize(string id, string data)
		{
			return true; // Addressables uses the async path; nothing to do without a ResourceManager
		}

		public AsyncOperationHandle<bool> InitializeAsync(ResourceManager rm, string id, string data)
		{
			Register(rm);
			return rm.CreateCompletedOperation(true, null);
		}

		/// <summary>Add the encrypted catalog providers to <paramref name="rm"/> once.</summary>
		public static void Register(ResourceManager rm)
		{
			if (!Has(rm, typeof(EncryptedBinaryCatalogProvider).FullName))
				rm.ResourceProviders.Add(new EncryptedBinaryCatalogProvider(rm));
			if (!Has(rm, typeof(EncryptedCatalogDataProvider).FullName))
				rm.ResourceProviders.Add(new EncryptedCatalogDataProvider(rm));
		}

		private static bool Has(ResourceManager rm, string providerId)
		{
			foreach (IResourceProvider provider in rm.ResourceProviders)
			{
				if (provider.ProviderId == providerId)
					return true;
			}
			return false;
		}
	}
}
