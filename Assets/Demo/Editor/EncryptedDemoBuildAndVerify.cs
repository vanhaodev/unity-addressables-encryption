using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using vanhaodev.addressablesencryption;
using vanhaodev.addressablesencryption.Editor;

namespace Demo.Editor
{
	/// <summary>
	/// One click: create demo prefabs, an encrypted group and a plain group, a demo scene, build with the
	/// encrypted build script, then check the bundle files on disk. Afterwards press Play (Use Existing Build).
	/// </summary>
	internal static class EncryptedDemoBuildAndVerify
	{
		private const string DemoSecret = "demo-secret";
		private const string Root = "Assets/Demo";
		private const string EncryptedGroupName = "Demo Encrypted";
		private const string PlainGroupName = "Demo Plain";
		private const string RemoteGroupName = "Demo Remote Encrypted";
		private const string RemoteLoadPath = "http://localhost:8765/[BuildTarget]";

		/// <summary>Batchmode entry (-executeMethod Demo.Editor.EncryptedDemoBuildAndVerify.RunBatch): exit code 0 = all checks passed.</summary>
		public static void RunBatch()
		{
			EditorApplication.Exit(BuildAndVerify() ? 0 : 1);
		}

		[MenuItem("Tools/Addressables Encryption/Demo/Build And Verify")]
		private static void Run() => BuildAndVerify();

		private static bool BuildAndVerify()
		{
			var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

			var keySettings = AddressablesEncryptionSetupMenu.Setup(settings);
			EnsureDemoKey(keySettings);

			var encryptedGroup = EnsureGroup(settings, EncryptedGroupName, true);
			var plainGroup = EnsureGroup(settings, PlainGroupName, false);
			var remoteGroup = EnsureGroup(settings, RemoteGroupName, true);
			MakeRemote(settings, remoteGroup);
			AddEntry(settings, EnsurePrefab("EncryptedCube", PrimitiveType.Cube), encryptedGroup, "EncryptedCube");
			AddEntry(settings, EnsurePrefab("PlainSphere", PrimitiveType.Sphere), plainGroup, "PlainSphere");
			AddEntry(settings, EnsurePrefab("RemoteCapsule", PrimitiveType.Capsule), remoteGroup, "RemoteCapsule");
			EnsureScene();
			UsePlayModeExistingBuild(settings);
			AssetDatabase.SaveAssets();

			AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
			if (!string.IsNullOrEmpty(result.Error))
			{
				Debug.LogError($"[Demo] Build failed: {result.Error}");
				return false;
			}

			return Verify(result, new[] { encryptedGroup, remoteGroup }, plainGroup);
		}

		/// <summary>
		/// Remote group served by the PlayMode tests' local HTTP server (http://localhost:8765).
		/// Changes the active profile's Remote.LoadPath: demo project only.
		/// </summary>
		private static void MakeRemote(AddressableAssetSettings settings, AddressableAssetGroup group)
		{
			settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, RemoteLoadPath);
			var bundled = group.GetSchema<BundledAssetGroupSchema>();
			bundled.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
			bundled.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
		}

		private static void EnsureDemoKey(AddressablesEncryptionSettings keySettings)
		{
			var so = new SerializedObject(keySettings);
			var key = so.FindProperty("m_Key");
			if (!string.IsNullOrEmpty(key.stringValue) && key.stringValue != DemoSecret)
				Debug.LogWarning($"[Demo] Build key is not '{DemoSecret}'; set the same secret on the demo component or loading fails.");
			if (string.IsNullOrEmpty(key.stringValue))
			{
				key.stringValue = DemoSecret;
				so.FindProperty("m_Format").enumValueIndex = (int)AddressablesEncryptionSettings.KeyFormat.Secret;
				so.ApplyModifiedPropertiesWithoutUndo();
			}
		}

		private static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string name, bool encrypted)
		{
			var group = settings.FindGroup(name) ??
				settings.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

			if (encrypted && group.GetSchema<EncryptedBundleGroupSchema>() == null)
				group.AddSchema<EncryptedBundleGroupSchema>();
			return group;
		}

		private static string EnsurePrefab(string name, PrimitiveType shape)
		{
			var path = $"{Root}/Prefabs/{name}.prefab";
			if (!File.Exists(path))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				var go = GameObject.CreatePrimitive(shape);
				go.name = name;
				PrefabUtility.SaveAsPrefabAsset(go, path);
				Object.DestroyImmediate(go);
			}
			return AssetDatabase.AssetPathToGUID(path);
		}

		private static void AddEntry(AddressableAssetSettings settings, string guid, AddressableAssetGroup group, string address)
		{
			var entry = settings.CreateOrMoveEntry(guid, group);
			entry.address = address;
		}

		private static void EnsureScene()
		{
			var path = $"{Root}/Scenes/EncryptedDemo.unity";
			if (File.Exists(path))
				return;

			Directory.CreateDirectory(Path.GetDirectoryName(path));
			var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
			new GameObject("EncryptedAddressablesDemo").AddComponent<Demo.EncryptedAddressablesDemo>();
			EditorSceneManager.SaveScene(scene, path);
		}

		private static void UsePlayModeExistingBuild(AddressableAssetSettings settings)
		{
			var index = settings.DataBuilders.FindIndex(b => b is BuildScriptPackedPlayMode);
			if (index >= 0)
				settings.ActivePlayModeDataBuilderIndex = index;
		}

		private static bool Verify(AddressablesPlayerBuildResult result, AddressableAssetGroup[] encryptedGroups, AddressableAssetGroup plainGroup)
		{
			var ok = true;
			var checkedCount = 0;
			var report = new StringBuilder("[Demo] Bundle check:\n");

			foreach (var bundle in result.AssetBundleBuildResults.Where(b => encryptedGroups.Contains(b.SourceAssetGroup) || b.SourceAssetGroup == plainGroup))
			{
				var bytes = File.ReadAllBytes(bundle.FilePath);
				var fileName = Path.GetFileName(bundle.FilePath);
				var hasUnityFs = bytes.Length >= 7 && Encoding.ASCII.GetString(bytes, 0, 7) == "UnityFS";
				var encrypted = EncryptedBundleFormat.IsEncrypted(bytes);
				// Encrypted groups hash their file names: no group name on disk or CDN.
				var nameHidden = !fileName.StartsWith("demo", System.StringComparison.OrdinalIgnoreCase);
				var expectEncrypted = encryptedGroups.Contains(bundle.SourceAssetGroup);
				var pass = expectEncrypted ? encrypted && !hasUnityFs && nameHidden : hasUnityFs && !encrypted;
				ok &= pass;
				checkedCount++;
				report.AppendLine($"  {(pass ? "PASS" : "FAIL")} {fileName} (group '{bundle.SourceAssetGroup.Name}', encrypted={encrypted}, UnityFS header={hasUnityFs}, name hidden={nameHidden})");
			}

			ok &= checkedCount >= 3; // at least one bundle per demo group
			if (ok)
				Debug.Log(report + "All good. Open Assets/Demo/Scenes/EncryptedDemo.unity and press Play (Play Mode Script: Use Existing Build).");
			else
				Debug.LogError(report.ToString());
			return ok;
		}
	}
}
