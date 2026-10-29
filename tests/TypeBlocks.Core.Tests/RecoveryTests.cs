using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using TypeBlocks.Core;

internal static class RecoveryTests
{
    private const string Password = "synthetic original recovery password";
    private const string NewPassword = "synthetic replacement recovery password";
    private const string Secret = "Synthetic recovery history must stay encrypted.";

    public static void Verify(Action<string, Action> run, string temporaryRoot)
    {
        string root = Path.Combine(temporaryRoot, "recovery");
        string baseline = Path.Combine(root, "baseline");
        var timestamp = new DateTimeOffset(new DateTime(2031, 1, 2, 12, 0, 0, DateTimeKind.Local));
        var date = DateOnly.FromDateTime(timestamp.LocalDateTime);
        KeystrokeEntry[] entries = [new(timestamp, "synthetic-notepad", "A", Secret, false)];
        using (var vault = VaultStore.Create(baseline, Password)) vault.Append(entries);
        run("legacy vault has no recovery and failed configuration preserves metadata", () =>
        {
            Assert(!VaultStore.HasRecoveryCode(baseline), "A legacy vault unexpectedly has recovery enabled.");
            byte[] before = File.ReadAllBytes(MetadataPath(baseline));
            var metadata = JsonNode.Parse(before)!;
            Assert(metadata["version"]!.GetValue<int>() == 1 && metadata["recovery"] is null, "Legacy metadata changed format.");
            Throws<InvalidOperationException>(() => VaultStore.RecoverPassword(baseline, new string('A', 64), NewPassword));
            Throws<CryptographicException>(() => VaultStore.CreateRecoveryCode(baseline, "incorrect password"));
            Assert(before.SequenceEqual(File.ReadAllBytes(MetadataPath(baseline))), "A failed recovery setup changed metadata.");
            using (var pending = VaultStore.PrepareRecoveryCode(baseline, Password))
                Assert(pending.Code.Length == 71 && !VaultStore.HasRecoveryCode(baseline)
                    && before.SequenceEqual(File.ReadAllBytes(MetadataPath(baseline))), "Preparing recovery modified a legacy vault before activation.");
            Assert(!VaultStore.HasRecoveryCode(baseline) && before.SequenceEqual(File.ReadAllBytes(MetadataPath(baseline))),
                "Canceling initial recovery configuration modified the legacy vault.");
        });

        string code = VaultStore.CreateRecoveryCode(baseline, Password);
        run("recovery configuration upgrades metadata and preserves password and recording access", () =>
        {
            Assert(code.Length == 71 && code.Split('-').All(group => group.Length == 8), "Recovery code formatting is invalid.");
            Assert(VaultStore.HasRecoveryCode(baseline), "Recovery configuration was not saved.");
            Assert(ReadMetadata(baseline)["version"]!.GetValue<int>() == 2, "Recovery metadata was not versioned.");
            VaultStore.VerifyPassword(baseline, Password);
            Assert(entries.SequenceEqual(VaultStore.ReadHistory(baseline, Password, date)), "Recovery setup changed history.");
            using var recorder = VaultStore.OpenForRecording(baseline);
            recorder.Append([entries[0] with { Text = "after recovery setup" }]);
            Assert(VaultStore.ReadHistory(baseline, Password, date).Count == 2, "Recovery setup prevented public-key recording.");
        });

        run("wrong malformed or invalid reset credentials do not modify metadata", () =>
        {
            string folder = Copy("invalid-reset");
            byte[] before = File.ReadAllBytes(MetadataPath(folder));
            foreach (string invalid in new[] { "", "not a recovery code", new string('A', 63), new string('A', 65), new string('A', 257), new string('0', 64) })
                Throws<CryptographicException>(() => VaultStore.RecoverPassword(folder, invalid, NewPassword));
            Throws<ArgumentException>(() => VaultStore.RecoverPassword(folder, code, "short"));
            Assert(before.SequenceEqual(File.ReadAllBytes(MetadataPath(folder))), "Invalid recovery changed metadata.");
            VaultStore.VerifyPassword(folder, Password);
        });

        run("recovery resets a forgotten password while preserving all history and public key", () =>
        {
            string folder = Copy("reset");
            string publicKey = ReadMetadata(folder)["publicKey"]!.GetValue<string>();
            byte[] history = File.ReadAllBytes(HistoryPath(folder));
            using var recorder = VaultStore.OpenForRecording(folder);
            VaultStore.RecoverPassword(folder, "  " + code.ToLowerInvariant().Replace('-', ' ') + "\n", NewPassword);
            Throws<CryptographicException>(() => VaultStore.VerifyPassword(folder, Password));
            VaultStore.VerifyPassword(folder, NewPassword);
            Assert(VaultStore.ReadHistory(folder, NewPassword, date).Count == 2, "Recovered history was lost.");
            Assert(publicKey == ReadMetadata(folder)["publicKey"]!.GetValue<string>() && history.SequenceEqual(File.ReadAllBytes(HistoryPath(folder))),
                "Password recovery changed history or its public key.");
            recorder.Append([entries[0] with { Text = "after password recovery" }]);
            Assert(VaultStore.ReadHistory(folder, NewPassword, date).Count == 3, "An existing recorder failed after recovery.");
            Assert(Directory.GetFiles(folder, "*.tmp").Length == 0, "Recovery left temporary metadata behind.");
        });

        run("normal password changes retain recovery access", () =>
        {
            string folder = Copy("change-password");
            string recoveryBefore = ReadMetadata(folder)["recovery"]!.ToJsonString();
            VaultStore.ChangePassword(folder, Password, NewPassword);
            Assert(recoveryBefore == ReadMetadata(folder)["recovery"]!.ToJsonString(), "Changing password altered the recovery envelope.");
            Throws<CryptographicException>(() => VaultStore.VerifyPassword(folder, Password));
            VaultStore.RecoverPassword(folder, code, Password);
            Assert(VaultStore.ReadHistory(folder, Password, date).Count == 2, "Password change invalidated the recovery code.");
        });

        run("regenerating recovery invalidates the old code for current metadata", () =>
        {
            string folder = Copy("replace-code");
            string replacement = VaultStore.CreateRecoveryCode(folder, Password);
            Assert(code != replacement, "Recovery regeneration reused a code.");
            byte[] before = File.ReadAllBytes(MetadataPath(folder));
            Throws<CryptographicException>(() => VaultStore.RecoverPassword(folder, code, NewPassword));
            Assert(before.SequenceEqual(File.ReadAllBytes(MetadataPath(folder))), "An obsolete recovery code modified metadata.");
            VaultStore.RecoverPassword(folder, replacement, NewPassword);
            Assert(VaultStore.ReadHistory(folder, NewPassword, date).Count == 2, "The replacement recovery code failed.");
        });

        run("canceling pending recovery preserves the old password and recovery code", () =>
        {
            string folder = Copy("cancel-pending");
            byte[] before = File.ReadAllBytes(MetadataPath(folder));
            var pending = VaultStore.PrepareRecoveryCode(folder, Password);
            string canceledCode = pending.Code;
            Assert(before.SequenceEqual(File.ReadAllBytes(MetadataPath(folder))), "Preparing a replacement immediately changed metadata.");
            pending.Dispose();
            pending.Dispose();
            Throws<ObjectDisposedException>(() => pending.Activate());
            Throws<ObjectDisposedException>(() => _ = pending.Code);
            Assert(before.SequenceEqual(File.ReadAllBytes(MetadataPath(folder))), "Canceling a recovery replacement changed metadata.");
            VaultStore.VerifyPassword(folder, Password);
            Throws<CryptographicException>(() => VaultStore.RecoverPassword(folder, canceledCode, NewPassword));
            VaultStore.RecoverPassword(folder, code, NewPassword);
            Assert(VaultStore.ReadHistory(folder, NewPassword, date).Count == 2, "Canceling preparation invalidated the saved recovery code.");
        });

        run("pending recovery activates only on confirmation and activation is idempotent", () =>
        {
            string folder = Copy("activate-pending");
            byte[] before = File.ReadAllBytes(MetadataPath(folder));
            using var pending = VaultStore.PrepareRecoveryCode(folder, Password);
            Assert(before.SequenceEqual(File.ReadAllBytes(MetadataPath(folder))), "Preparing recovery changed current metadata.");
            pending.Activate();
            byte[] activated = File.ReadAllBytes(MetadataPath(folder));
            Assert(!before.SequenceEqual(activated), "Activating recovery did not save the replacement.");
            pending.Activate();
            Assert(activated.SequenceEqual(File.ReadAllBytes(MetadataPath(folder))), "Repeated activation rewrote the vault.");
            VaultStore.VerifyPassword(folder, Password);
            Throws<CryptographicException>(() => VaultStore.RecoverPassword(folder, code, NewPassword));
            VaultStore.RecoverPassword(folder, pending.Code, NewPassword);
            Assert(VaultStore.ReadHistory(folder, NewPassword, date).Count == 2, "The activated code did not recover history.");
        });

        run("pending recovery refuses to overwrite an intervening password change", () =>
        {
            string folder = Copy("stale-pending");
            using var pending = VaultStore.PrepareRecoveryCode(folder, Password);
            VaultStore.ChangePassword(folder, Password, NewPassword);
            byte[] changed = File.ReadAllBytes(MetadataPath(folder));
            Throws<InvalidOperationException>(() => pending.Activate());
            Assert(changed.SequenceEqual(File.ReadAllBytes(MetadataPath(folder))), "Stale recovery preparation overwrote changed metadata.");
            VaultStore.VerifyPassword(folder, NewPassword);
            Throws<CryptographicException>(() => VaultStore.VerifyPassword(folder, Password));
            VaultStore.RecoverPassword(folder, code, Password);
            Assert(VaultStore.ReadHistory(folder, Password, date).Count == 2, "An intervening change lost recovery access.");
        });

        run("recovery envelope tampering fails password and recovery authentication", () =>
        {
            foreach (string field in new[] { "nonce", "tag", "encryptedPrivateKey" })
            {
                string folder = Copy("tamper-" + field);
                var metadata = ReadMetadata(folder);
                byte[] data = Convert.FromBase64String(metadata["recovery"]![field]!.GetValue<string>());
                data[0] ^= 1;
                metadata["recovery"]![field] = Convert.ToBase64String(data);
                SaveMetadata(folder, metadata);
                byte[] before = File.ReadAllBytes(MetadataPath(folder));
                Throws<CryptographicException>(() => VaultStore.VerifyPassword(folder, Password));
                Throws<CryptographicException>(() => VaultStore.RecoverPassword(folder, code, NewPassword));
                Assert(before.SequenceEqual(File.ReadAllBytes(MetadataPath(folder))), "Tampered recovery modified metadata.");
            }
        });

        run("recovery metadata rejects missing malformed duplicate and unsupported fields", () =>
        {
            Action<JsonNode>[] mutations =
            [
                metadata => metadata["recovery"] = null,
                metadata => metadata["version"] = 1,
                metadata => metadata["recovery"]!["nonce"] = Convert.ToBase64String(new byte[11]),
                metadata => metadata["recovery"]!["tag"] = "invalid base64",
                metadata => metadata["recovery"]!["encryptedPrivateKey"] = Convert.ToBase64String(new byte[127]),
                metadata => metadata["recovery"]!["unknown"] = true
            ];
            for (int index = 0; index < mutations.Length; index++)
            {
                string folder = Copy("malformed-" + index);
                var metadata = ReadMetadata(folder);
                mutations[index](metadata);
                SaveMetadata(folder, metadata);
                Throws<InvalidDataException>(() => VaultStore.OpenForRecording(folder));
            }
            string duplicate = Copy("duplicate-recovery");
            string json = File.ReadAllText(MetadataPath(duplicate));
            File.WriteAllText(MetadataPath(duplicate), json.Replace("\"recovery\":{", "\"recovery\":{\"nonce\":\"duplicate\","));
            Throws<InvalidDataException>(() => VaultStore.OpenForRecording(duplicate));
        });

        run("recovery code and decrypted private key never appear in stored vault bytes", () =>
        {
            var metadata = ReadMetadata(baseline);
            byte[] key = Convert.FromHexString(code.Replace("-", ""));
            byte[] privateKey = DecryptRecoveryPrivateKey(metadata, key);
            try
            {
                foreach (string path in Directory.GetFiles(baseline))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    string text = Encoding.UTF8.GetString(bytes);
                    foreach (string secret in new[] { code, code.Replace("-", ""), Convert.ToBase64String(key), Password, Secret, Convert.ToBase64String(privateKey) })
                        Assert(!text.Contains(secret, StringComparison.Ordinal), "A secret appeared in " + Path.GetFileName(path));
                    Assert(bytes.AsSpan().IndexOf(key) < 0 && bytes.AsSpan().IndexOf(privateKey) < 0, "Raw recovery/private key bytes were stored.");
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(privateKey); }
        });

        string Copy(string name)
        {
            string folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            foreach (string path in Directory.GetFiles(baseline)) File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
            return folder;
        }
        string HistoryPath(string folder) => Path.Combine(folder, date.ToString("yyyy-MM-dd") + ".tmh");
    }

    private static byte[] DecryptRecoveryPrivateKey(JsonNode metadata, byte[] key)
    {
        byte[] publicKey = Convert.FromBase64String(metadata["publicKey"]!.GetValue<string>());
        using var aad = new MemoryStream();
        using (var writer = new BinaryWriter(aad, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("TypeBlocks recovery private key");
            writer.Write(2);
            writer.Write(publicKey.Length);
            writer.Write(publicKey);
        }
        var recovery = metadata["recovery"]!;
        byte[] cipher = Convert.FromBase64String(recovery["encryptedPrivateKey"]!.GetValue<string>());
        byte[] plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(Convert.FromBase64String(recovery["nonce"]!.GetValue<string>()), cipher,
            Convert.FromBase64String(recovery["tag"]!.GetValue<string>()), plaintext, aad.ToArray());
        return plaintext;
    }

    private static string MetadataPath(string directory) => Path.Combine(directory, "vault.json");
    private static JsonNode ReadMetadata(string directory) => JsonNode.Parse(File.ReadAllText(MetadataPath(directory)))!;
    private static void SaveMetadata(string directory, JsonNode metadata) => File.WriteAllText(MetadataPath(directory), metadata.ToJsonString());
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + ".");
    }
}
