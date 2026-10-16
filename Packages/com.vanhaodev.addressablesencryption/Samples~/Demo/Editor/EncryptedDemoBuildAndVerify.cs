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

namespace vanhaodev.addressablesencryption.Samples.Editor
{
	/// <summary>
	/// One click: create demo prefabs, an encrypted group and a plain group, a demo scene, build with the
	/// encrypted build script, then check the bundle files on disk. Afterwards press Play (Use Existing Build).
	/// </summary>
	internal static class EncryptedDemoBuildAndVerify
	{
		private const string DemoSecret = "demo-secret";
		/// <summary>
		/// The imported sample folder (e.g. Assets/Samples/Addressables Encryption/1.1.0/Demo), found from this
		/// script's location so the sample works wherever Package Manager imports it.
		/// </summary>
		private static string Root
		{
			get
			{
				var guid = AssetDatabase.FindAssets(nameof(EncryptedDemoBuildAndVerify) + " t:MonoScript").First();
				var editorDir = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
				return Path.GetDirectoryName(editorDir).Replace('\\', '/');
			}
		}
		private const string EncryptedGroupName = "Demo Encrypted";
		private const string PlainGroupName = "Demo Plain";
		private const string RemoteGroupName = "Demo Remote Encrypted";
		private const string RemoteLoadPath = "http://localhost:8765/[BuildTarget]";

		/// <summary>Batchmode entry (-executeMethod vanhaodev.addressablesencryption.Samples.Editor.EncryptedDemoBuildAndVerify.RunBatch): exit code 0 = all checks passed.</summary>
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
			// Same picture twice: encrypted vs plain, to compare what a ripper gets out of the player build.
			AddEntry(settings, AssetDatabase.AssetPathToGUID($"{Root}/Images/hanni_pham.jpg"), encryptedGroup, "HanniEncrypted");
			AddEntry(settings, AssetDatabase.AssetPathToGUID($"{Root}/Images/hanni_pham_plain.jpg"), plainGroup, "HanniPlain");
			EnsureScene();
			EnsureImageScene();
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
			new GameObject("EncryptedAddressablesDemo").AddComponent<EncryptedAddressablesDemo>();
			EditorSceneManager.SaveScene(scene, path);
		}

		private static string ImageScenePath => Root + "/Scenes/EncryptedImageDemo.unity";

		private static void EnsureImageScene()
		{
			if (File.Exists(ImageScenePath))
				return;

			Directory.CreateDirectory(Path.GetDirectoryName(ImageScenePath));
			var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
			new GameObject("EncryptedImageDemo").AddComponent<EncryptedImageDemo>();
			EditorSceneManager.SaveScene(scene, ImageScenePath);
		}

		/// <summary>
		/// Batchmode: Build And Verify, then a Windows player of the image demo.
		/// -executeMethod vanhaodev.addressablesencryption.Samples.Editor.EncryptedDemoBuildAndVerify.RunBatchPlayer -buildOutput &lt;path to .exe&gt;
		/// </summary>
		public static void RunBatchPlayer()
		{
			if (!BuildAndVerify())
			{
				EditorApplication.Exit(1);
				return;
			}

			var args = System.Environment.GetCommandLineArgs();
			var i = System.Array.IndexOf(args, "-buildOutput");
			var output = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Builds/Windows/AddressablesEncryptionDemo.exe";

			var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
			{
				scenes = new[] { ImageScenePath },
				locationPathName = output,
				target = BuildTarget.StandaloneWindows64,
				options = BuildOptions.None
			});
			Debug.Log($"[Demo] Player build: {report.summary.result} -> {output}");
			EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
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
				Debug.Log(report + $"All good. Open {Root}/Scenes/EncryptedImageDemo.unity (or EncryptedDemo.unity) and press Play (Play Mode Script: Use Existing Build).");
			else
				Debug.LogError(report.ToString());
			return ok;
		}
	}
}
