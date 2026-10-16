using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.ResourceManagement.Util;

namespace vanhaodev.addressablesencryption.Editor
{
	/// <summary>
	/// Registers <see cref="EncryptedCatalogInitialization"/> in the built settings.json, so Addressables creates
	/// the encrypted catalog providers before it loads the catalog. Addressables gives no hook between creating
	/// its runtime data and writing the file, so the file is patched after the build with the same serializer.
	/// </summary>
	internal static class EncryptedCatalogRuntimeSettings
	{
		public const string FileName = "settings.json";

		public static void AddInitializationObject(string buildPath)
		{
			var path = Path.Combine(buildPath, FileName);
			var runtimeData = JsonUtility.FromJson<ResourceManagerRuntimeData>(File.ReadAllText(path));
			var init = ObjectInitializationData.CreateSerializedInitializationData<EncryptedCatalogInitialization>();

			foreach (var existing in runtimeData.InitializationObjects)
			{
				if (existing.Id == init.Id)
					return;
			}
			runtimeData.InitializationObjects.Add(init);
			File.WriteAllText(path, JsonUtility.ToJson(runtimeData));
		}
	}
}
