using System.Reflection;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace vanhaodev.addressablesencryption
{
	/// <summary>
	/// ProvideHandle.IsValid is internal to the ResourceManager. Provider operations are pooled, so completing
	/// a handle whose operation was released (and maybe reused) must be avoided. Read it once via reflection;
	/// if a future Addressables version removes it, assume valid (same behaviour as providers without the check).
	/// </summary>
	internal static class ProvideHandleUtil
	{
		private static readonly PropertyInfo IsValidProperty =
			typeof(ProvideHandle).GetProperty("IsValid", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

		public static bool IsValid(ProvideHandle handle)
		{
			return IsValidProperty == null || (bool)IsValidProperty.GetValue(handle);
		}
	}
}
