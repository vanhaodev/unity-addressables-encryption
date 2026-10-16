using System.IO;
using NUnit.Framework;

namespace vanhaodev.addressablesencryption.Tests
{
	public class EncryptedBundleCacheTests
	{
		private string _previousRoot;
		private string _root;

		[SetUp]
		public void SetUp()
		{
			_previousRoot = EncryptedBundleCache.Root;
			_root = Path.Combine(Path.GetTempPath(), "aaen-cache-tests-" + System.Guid.NewGuid().ToString("N"));
			EncryptedBundleCache.Root = _root;
		}

		[TearDown]
		public void TearDown()
		{
			EncryptedBundleCache.Clear();
			EncryptedBundleCache.Root = _previousRoot;
		}

		[Test]
		public void NoHash_NoCachePath()
		{
			Assert.IsNull(EncryptedBundleCache.GetPath("characters", ""));
			Assert.IsNull(EncryptedBundleCache.GetPath(null, "abc"));
			Assert.IsNull(EncryptedBundleCache.GetPath("characters", "00000000000000000000000000000000"), "cache disabled in binary catalog");
		}

		[Test]
		public void MixedSlashRoot_StoreKeepsNewFile()
		{
			// persistentDataPath on Windows uses '/', which used to make Store delete the file it just wrote.
			EncryptedBundleCache.Root = _root.Replace('\\', '/');
			var path = EncryptedBundleCache.GetPath("characters", "v1");
			EncryptedBundleCache.Store(path, new byte[] { 1 });
			Assert.IsTrue(EncryptedBundleCache.IsCached("characters", "v1"));
		}

		[Test]
		public void Store_ThenIsCached()
		{
			var path = EncryptedBundleCache.GetPath("characters", "hash1");
			Assert.IsFalse(EncryptedBundleCache.IsCached("characters", "hash1"));

			EncryptedBundleCache.Store(path, new byte[] { 1, 2, 3 });

			Assert.IsTrue(EncryptedBundleCache.IsCached("characters", "hash1"));
			Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3 }));
		}

		[Test]
		public void NewVersion_DeletesOldVersion_OtherBundlesKept()
		{
			EncryptedBundleCache.Store(EncryptedBundleCache.GetPath("characters", "v1"), new byte[] { 1 });
			EncryptedBundleCache.Store(EncryptedBundleCache.GetPath("weapons", "v1"), new byte[] { 2 });

			EncryptedBundleCache.Store(EncryptedBundleCache.GetPath("characters", "v2"), new byte[] { 3 });

			Assert.IsFalse(EncryptedBundleCache.IsCached("characters", "v1"));
			Assert.IsTrue(EncryptedBundleCache.IsCached("characters", "v2"));
			Assert.IsTrue(EncryptedBundleCache.IsCached("weapons", "v1"));
		}

		[Test]
		public void Path_HidesBundleNameAndHash()
		{
			var path = EncryptedBundleCache.GetPath("characters_assets_all", "0123abcd");
			var relative = path.Substring(_root.Length);
			StringAssert.DoesNotContain("characters", relative);
			StringAssert.DoesNotContain("0123abcd", relative);
		}

		[Test]
		public void Store_LeavesNoTempFiles_AndClearRemovesAll()
		{
			EncryptedBundleCache.Store(EncryptedBundleCache.GetPath("characters", "v1"), new byte[] { 1 });
			Assert.IsEmpty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));

			EncryptedBundleCache.Clear();
			Assert.IsFalse(Directory.Exists(_root));
		}
	}
}
