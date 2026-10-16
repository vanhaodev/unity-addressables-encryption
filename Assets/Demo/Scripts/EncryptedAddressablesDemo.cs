using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using vanhaodev.addressablesencryption;

namespace Demo
{
	/// <summary>
	/// Loads one asset from an encrypted group and one from a plain group, then instantiates both.
	/// Test with Play Mode Script = "Use Existing Build" after building Addressables with the encrypted build script.
	/// </summary>
	public class EncryptedAddressablesDemo : MonoBehaviour
	{
		[SerializeField] private string m_Secret = "demo-secret";
		[SerializeField] private string m_EncryptedAddress = "EncryptedCube";
		[SerializeField] private string m_PlainAddress = "PlainSphere";

		private void Start()
		{
			// Hardcoded here; a game could fetch the secret from its server first, then call SetKey.
			AddressablesEncryption.SetKey(m_Secret);

			Load(m_EncryptedAddress, new Vector3(-1.5f, 0, 0));
			Load(m_PlainAddress, new Vector3(1.5f, 0, 0));
		}

		private static void Load(string address, Vector3 position)
		{
			Addressables.InstantiateAsync(address, position, Quaternion.identity).Completed += handle =>
			{
				if (handle.Status == AsyncOperationStatus.Succeeded)
					Debug.Log($"[Demo] Loaded '{address}'");
				else
					Debug.LogError($"[Demo] Failed to load '{address}': {handle.OperationException}");
			};
		}
	}
}
