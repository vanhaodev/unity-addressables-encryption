using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.TestTools;
using vanhaodev.addressablesencryption.Editor;

namespace vanhaodev.addressablesencryption.Tests
{
	/// <summary>
	/// AudioClips of every load type, Streaming included, build into an encrypted bundle.
	/// </summary>
	public class StreamingAudioEncryptedBuildTests
	{
		private const string Root = "Assets/AddressablesEncryptionTestContent";
		private const string Folder = Root + "/Audio";
		private const string ClipPath = Folder + "/Tone.wav";
		private const string GroupName = "AE Audio Test";

		private AddressableAssetSettings _settings;
		private AddressableAssetGroup _group;
		private int _savedBuilderIndex;

		[SetUp]
		public void SetUp()
		{
			_settings = AddressableAssetSettingsDefaultObject.Settings;
			Assert.IsNotNull(_settings, "Addressables settings not found.");
			LogAssert.ignoreFailingMessages = true;
			_savedBuilderIndex = _settings.ActivePlayerDataBuilderIndex;
			AddressablesEncryptionSetupMenu.Setup(_settings);

			Directory.CreateDirectory(Folder);
			File.WriteAllBytes(ClipPath, MakeWav(seconds: 1, sampleRate: 8000));
			AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceSynchronousImport);

			_group = _settings.CreateGroup(GroupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
			_group.AddSchema<EncryptedBundleGroupSchema>();
			_settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(ClipPath), _group).address = "Tone";
			AssetDatabase.SaveAssets();
		}

		[TearDown]
		public void TearDown()
		{
			LogAssert.ignoreFailingMessages = false;
			if (_group != null)
				_settings.RemoveGroup(_group);
			_settings.ActivePlayerDataBuilderIndex = _savedBuilderIndex;
			AssetDatabase.DeleteAsset(Folder);
			if (Directory.Exists(Root) && Directory.GetFileSystemEntries(Root).Length == 0)
				AssetDatabase.DeleteAsset(Root);
			AssetDatabase.SaveAssets();
		}

		// Streaming clips are read through the decrypting stream while they play (PlayMode test with a real song).
		[TestCase(AudioClipLoadType.Streaming)]
		[TestCase(AudioClipLoadType.CompressedInMemory)]
		[TestCase(AudioClipLoadType.DecompressOnLoad)]
		public void AnyLoadType_BuildsEncrypted(AudioClipLoadType loadType)
		{
			SetLoadType(loadType);

			AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
			Assert.IsTrue(string.IsNullOrEmpty(result.Error), "build failed: " + result.Error);

			var bundles = result.AssetBundleBuildResults.Where(b => b.SourceAssetGroup == _group).Select(b => b.FilePath).Distinct().ToList();
			Assert.AreEqual(1, bundles.Count);
			Assert.IsTrue(EncryptedBundleFormat.IsEncryptedFile(bundles[0]), "audio bundle is encrypted");
		}

		private static void SetLoadType(AudioClipLoadType loadType)
		{
			var importer = (AudioImporter)AssetImporter.GetAtPath(ClipPath);
			var sampleSettings = importer.defaultSampleSettings;
			sampleSettings.loadType = loadType;
			importer.defaultSampleSettings = sampleSettings;
			importer.SaveAndReimport();
		}

		/// <summary>Mono 16-bit PCM sine wave, the smallest WAV Unity imports as an AudioClip.</summary>
		private static byte[] MakeWav(int seconds, int sampleRate)
		{
			var samples = seconds * sampleRate;
			var dataSize = samples * 2;
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			writer.Write("RIFF".ToCharArray());
			writer.Write(36 + dataSize);
			writer.Write("WAVE".ToCharArray());
			writer.Write("fmt ".ToCharArray());
			writer.Write(16);            // PCM chunk size
			writer.Write((short)1);      // PCM format
			writer.Write((short)1);      // mono
			writer.Write(sampleRate);
			writer.Write(sampleRate * 2); // byte rate
			writer.Write((short)2);      // block align
			writer.Write((short)16);     // bits per sample
			writer.Write("data".ToCharArray());
			writer.Write(dataSize);
			for (int i = 0; i < samples; i++)
				writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * short.MaxValue * 0.5));
			writer.Flush();
			return stream.ToArray();
		}
	}
}
