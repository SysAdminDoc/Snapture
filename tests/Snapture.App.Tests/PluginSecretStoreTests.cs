using System.Text;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PluginSecretStoreTests
{
    [TestMethod]
    public void SecretsRoundTripThroughUserScopedEncryptedStorage()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture-SecretTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string key = "api-token";
            const string value = "secret-value-that-must-not-be-plaintext";
            using (var store = new PluginSecretStore(root, "Example Uploader"))
            {
                store.SetSecret(key, value);
                Assert.IsTrue(store.TryGetSecret(key, out var loaded));
                Assert.AreEqual(value, loaded);
                CollectionAssert.Contains(store.Keys.ToArray(), key);
            }

            var file = Directory.EnumerateFiles(Path.Combine(root, "plugin-secrets"), "*.bin").Single();
            var raw = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.IsFalse(raw.Contains(value, StringComparison.Ordinal));

            using (var reopened = new PluginSecretStore(root, "Example Uploader"))
            {
                Assert.IsTrue(reopened.TryGetSecret(key, out var loaded));
                Assert.AreEqual(value, loaded);
                Assert.IsTrue(reopened.RemoveSecret(key));
                Assert.IsFalse(reopened.TryGetSecret(key, out _));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
