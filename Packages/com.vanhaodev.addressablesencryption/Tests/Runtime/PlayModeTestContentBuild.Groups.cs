#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using vanhaodev.addressablesencryption.Editor;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>Temporary test groups and their generated textures (Editor-only part of <see cref="PlayModeTestContentBuild"/>).</summary>
	public partial class PlayModeTestContentBuild
	{
		private const string RemoteTexturePath = ContentFolder + "/RemoteTexture.png";
		private const string LargeTexturePath = ContentFolder + "/LargeTexture.png";

		/// <summary>Encrypted remote group with "Use Asset Bundle Cache", loaded from the local HTTP server.</summary>
		private static void CreateRemoteGroup(AddressableAssetSettings settings)
		{
			WriteTexture(RemoteTexturePath, 256, 1234);
			var group = EnsureEncryptedGroup(settings, RemoteGroupName);

			SessionState.SetString(SavedLoadPathKey, settings.profileSettings.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath) ?? "");
			settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, RemoteLoadPath);

			var bundled = group.GetSchema<BundledAssetGroupSchema>();
			bundled.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
			bundled.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
			bundled.UseAssetBundleCache = true;

			AddEntry(settings, group, RemoteTexturePath, RemoteAddress);
		}

		/// <summary>Encrypted local group holding one ~16 MB uncompressed texture in an uncompressed bundle (memory test).</summary>
		private static void CreateLargeGroup(AddressableAssetSettings settings)
		{
			WriteTexture(LargeTexturePath, LargeTextureSize, 99);
			var group = EnsureEncryptedGroup(settings, LargeGroupName);

			var bundled = group.GetSchema<BundledAssetGroupSchema>();
			bundled.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
			bundled.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
			bundled.Compression = BundledAssetGroupSchema.BundleCompressionMode.Uncompressed;

			AddEntry(settings, group, LargeTexturePath, LargeAddress);
		}

		internal static AddressableAssetGroup EnsureEncryptedGroup(AddressableAssetSettings settings, string name)
		{
			var group = settings.FindGroup(name) ??
				settings.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
			if (group.GetSchema<EncryptedBundleGroupSchema>() == null)
				group.AddSchema<EncryptedBundleGroupSchema>();
			return group;
		}

		internal static void AddEntry(AddressableAssetSettings settings, AddressableAssetGroup group, string assetPath, string address)
		{
			var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(assetPath), group);
			entry.address = address;
		}

		private static void UsePlayModeExistingBuild(AddressableAssetSettings settings)
		{
			SessionState.SetInt(SavedPlayModeBuilderKey, settings.ActivePlayModeDataBuilderIndex);
			var index = settings.DataBuilders.FindIndex(b => b is BuildScriptPackedPlayMode);
			if (index >= 0)
				settings.ActivePlayModeDataBuilderIndex = index;
		}

		/// <summary>Square noise texture, imported uncompressed without mipmaps: its bundle size is exactly size² × 4 bytes.</summary>
		internal static void WriteTexture(string path, int size, int seed)
		{
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
			var pixels = new Color32[size * size];
			var random = new System.Random(seed);
			for (int i = 0; i < pixels.Length; i++)
				pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
			texture.SetPixels32(pixels);
			texture.Apply();
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);

			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
			var importer = (TextureImporter)AssetImporter.GetAtPath(path);
			importer.textureCompression = TextureImporterCompression.Uncompressed;
			importer.mipmapEnabled = false;
			importer.maxTextureSize = size;
			importer.SaveAndReimport();
		}
	}
}
#endif
