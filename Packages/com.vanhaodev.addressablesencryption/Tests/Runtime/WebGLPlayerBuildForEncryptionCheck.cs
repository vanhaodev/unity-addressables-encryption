#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using vanhaodev.addressablesencryption.Editor;

namespace vanhaodev.addressablesencryption.Tests.Runtime
{
	/// <summary>
	/// Batchmode helper that builds a WebGL player exercising the whole package on WebGL: encrypted catalog,
	/// local encrypted bundle (StreamingAssets URL), remote encrypted bundle (same-origin ServerData folder,
	/// cached in IndexedDB) and a 16 MB bundle for the main-thread hitch measurement.<br/>
	/// <c>Unity -batchmode -quit -buildTarget WebGL -executeMethod vanhaodev.addressablesencryption.Tests.Runtime.WebGLPlayerBuildForEncryptionCheck.Run -webglOutput &lt;folder&gt;</c><br/>
	/// Serve the output folder on port 8000 and open it in a browser; the console shows "[WebGLCheck] ..." lines.
	/// </summary>
	public static class WebGLPlayerBuildForEncryptionCheck
	{
		public const string RemoteLoadPath = "http://localhost:8000/ServerData/[BuildTarget]";
		private const string RemoteGroupName = "AE WebGL Remote";
		private const string LargeGroupName = "AE WebGL Large";
		private const string Folder = "Assets/AddressablesEncryptionTestContent/WebGL";
		private const string ScenePath = Folder + "/WebGLLoadCheck.unity";
		private const string DefaultOutput = "Builds/WebGL";

		public static void Run()
		{
			var ok = false;
			try
			{
				ok = Build(ArgumentAfter("-webglOutput") ?? DefaultOutput);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			if (Application.isBatchMode)
				EditorApplication.Exit(ok ? 0 : 1);
		}

		private static bool Build(string output)
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			var keySettings = AddressablesEncryptionSetupMenu.Setup(settings);
			if (keySettings.Key != PlayModeTestContentBuild.Secret)
				throw new InvalidOperationException($"Build key must be '{PlayModeTestContentBuild.Secret}'.");

			var savedEncryptCatalog = keySettings.EncryptCatalog;
			var savedLoadPath = settings.profileSettings.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath) ?? "";
			var savedCompression = PlayerSettings.WebGL.compressionFormat;
			var demoSchema = settings.FindGroup("Demo")?.GetSchema<EncryptedBundleGroupSchema>();
			var savedDemoEncryption = demoSchema != null && demoSchema.IsEnabled;
			try
			{
				keySettings.EncryptCatalog = true;
				if (demoSchema != null)
					demoSchema.IsEnabled = true; // the check must load an encrypted local bundle
				settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, RemoteLoadPath);
				Directory.CreateDirectory(Folder);
				CreateGroups(settings);
				CreateScene();
				AssetDatabase.SaveAssets();

				AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
				if (!string.IsNullOrEmpty(result.Error))
					throw new InvalidOperationException("Addressables build failed: " + result.Error);

				PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; // plain static server, no Content-Encoding
				var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
				{
					scenes = new[] { ScenePath },
					locationPathName = output,
					target = BuildTarget.WebGL,
					options = BuildOptions.IncludeTestAssemblies | BuildOptions.Development // Development: Debug.Log reaches the browser console
				});
				if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
					throw new InvalidOperationException("WebGL player build: " + report.summary.result);

				CopyDirectory(Path.Combine("ServerData", "WebGL"), Path.Combine(output, "ServerData", "WebGL"));
				Debug.Log($"[WebGLCheck] player built at '{output}'. Serve it on port 8000 (npx http-server <folder> -p 8000 -c-1) and open http://localhost:8000/");
				return true;
			}
			finally
			{
				// The player build reloads assets: objects fetched before it may be destroyed, so look them up again.
				PlayerSettings.WebGL.compressionFormat = savedCompression;
				settings = AddressableAssetSettingsDefaultObject.Settings;
				settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, savedLoadPath);
				AddressablesEncryptionSettings.Find().EncryptCatalog = savedEncryptCatalog;
				var demo = settings.FindGroup("Demo")?.GetSchema<EncryptedBundleGroupSchema>();
				if (demo != null)
					demo.IsEnabled = savedDemoEncryption;
				PlayModeTestContentBuild.RemoveGroup(settings, RemoteGroupName);
				PlayModeTestContentBuild.RemoveGroup(settings, LargeGroupName);
				AssetDatabase.DeleteAsset(Folder);
				AssetDatabase.SaveAssets();
			}
		}

		private static void CreateGroups(AddressableAssetSettings settings)
		{
			var remotePath = Folder + "/RemoteTexture.png";
			PlayModeTestContentBuild.WriteTexture(remotePath, 256, 4321);
			var remote = PlayModeTestContentBuild.EnsureEncryptedGroup(settings, RemoteGroupName);
			var bundled = remote.GetSchema<BundledAssetGroupSchema>();
			bundled.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
			bundled.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
			bundled.UseAssetBundleCache = true;
			PlayModeTestContentBuild.AddEntry(settings, remote, remotePath, WebGLLoadCheckBehaviour.RemoteAddress);

			var largePath = Folder + "/LargeTexture.png";
			PlayModeTestContentBuild.WriteTexture(largePath, PlayModeTestContentBuild.LargeTextureSize, 99);
			var large = PlayModeTestContentBuild.EnsureEncryptedGroup(settings, LargeGroupName);
			large.GetSchema<BundledAssetGroupSchema>().Compression = BundledAssetGroupSchema.BundleCompressionMode.Uncompressed;
			PlayModeTestContentBuild.AddEntry(settings, large, largePath, PlayModeTestContentBuild.LargeAddress);
		}

		private static void CreateScene()
		{
			var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var camera = new GameObject("Main Camera", typeof(Camera));
			camera.tag = "MainCamera";
			camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
			new GameObject("WebGLLoadCheck", typeof(WebGLLoadCheckBehaviour));
			EditorSceneManager.SaveScene(scene, ScenePath);
		}

		private static void CopyDirectory(string from, string to)
		{
			Directory.CreateDirectory(to);
			foreach (var file in Directory.GetFiles(from))
				File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
		}

		private static string ArgumentAfter(string name)
		{
			var args = Environment.GetCommandLineArgs();
			var i = Array.IndexOf(args, name);
			return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
		}
	}
}
#endif
