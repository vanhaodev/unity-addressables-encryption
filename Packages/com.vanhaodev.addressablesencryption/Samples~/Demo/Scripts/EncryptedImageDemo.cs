using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using vanhaodev.addressablesencryption;

namespace vanhaodev.addressablesencryption.Samples
{
	/// <summary>
	/// Shows the same picture loaded from an encrypted group (left) and a plain group (right).
	/// Both look identical in game; only the plain one can be pulled out of the build by a ripper.
	/// </summary>
	public class EncryptedImageDemo : MonoBehaviour
	{
		[SerializeField] private string m_Secret = "demo-secret";
		[SerializeField] private string m_EncryptedAddress = "HanniEncrypted";
		[SerializeField] private string m_PlainAddress = "HanniPlain";

		private Texture2D _encrypted;
		private Texture2D _plain;
		private string _status = "Loading...";

		private void Start()
		{
			AddressablesEncryption.SetKey(m_Secret);
			Load(m_EncryptedAddress, t => _encrypted = t);
			Load(m_PlainAddress, t => _plain = t);
		}

		private void Load(string address, System.Action<Texture2D> done)
		{
			Addressables.LoadAssetAsync<Texture2D>(address).Completed += handle =>
			{
				if (handle.Status == AsyncOperationStatus.Succeeded)
				{
					done(handle.Result);
					_status = "Loaded";
				}
				else
				{
					_status = $"Failed '{address}': {handle.OperationException?.Message}";
					Debug.LogError($"[Demo] {_status}");
				}
			};
		}

		private void OnGUI()
		{
			var half = Screen.width / 2f;
			Draw(new Rect(0, 0, half, Screen.height), _encrypted, "ENCRYPTED group");
			Draw(new Rect(half, 0, half, Screen.height), _plain, "PLAIN group");
			GUI.Label(new Rect(10, Screen.height - 30, Screen.width, 30), _status);
		}

		private static void Draw(Rect area, Texture2D texture, string label)
		{
			GUI.Label(new Rect(area.x + 10, area.y + 10, area.width, 30), label);
			if (texture != null)
				GUI.DrawTexture(new Rect(area.x + 10, area.y + 40, area.width - 20, area.height - 80), texture, ScaleMode.ScaleToFit);
		}
	}
}
