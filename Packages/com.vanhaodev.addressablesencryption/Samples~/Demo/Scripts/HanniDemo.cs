using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using vanhaodev.addressablesencryption;

namespace vanhaodev.addressablesencryption.Samples
{
	/// <summary>
	/// Loads one picture by address and draws it. Whether the bundle is encrypted is decided by the
	/// Addressables group (Encryption schema on/off) — this code is the same either way.
	/// </summary>
	public class HanniDemo : MonoBehaviour
	{
		[SerializeField] private string m_Secret = "demo-secret";
		[SerializeField] private string m_Address = "Hanni";

		private Texture2D _texture;
		private string _status = "Loading...";

		private void Start()
		{
			// Must match the key in AddressablesEncryptionSettings. Harmless when the group is not encrypted.
			AddressablesEncryption.SetKey(m_Secret);

			Addressables.LoadAssetAsync<Texture2D>(m_Address).Completed += handle =>
			{
				if (handle.Status == AsyncOperationStatus.Succeeded)
				{
					_texture = handle.Result;
					_status = "Loaded";
				}
				else
				{
					_status = $"Failed: {handle.OperationException?.Message}";
					Debug.LogError($"[Demo] {_status}");
				}
			};
		}

		private void OnGUI()
		{
			if (_texture != null)
				GUI.DrawTexture(new Rect(10, 10, Screen.width - 20, Screen.height - 50), _texture, ScaleMode.ScaleToFit);
			GUI.Label(new Rect(10, Screen.height - 30, Screen.width, 30), _status);
		}
	}
}
