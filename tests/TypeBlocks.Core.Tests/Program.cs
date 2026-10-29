using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using TypeBlocks.Core;

const string Password = "orange river window bridge";
const string NewPassword = "violet forest lantern stone";
const string Secret = "A unique private sentence that must never appear in stored bytes.";
var root = Path.Combine(Path.GetTempPath(), "TypeBlocks-tests-" + Guid.NewGuid().ToString("N"));
var baseline = Path.Combine(root, "baseline");
Directory.CreateDirectory(root);
var localTime = new DateTimeOffset(new DateTime(2031, 1, 2, 12, 0, 0, DateTimeKind.Local));
var date = DateOnly.FromDateTime(localTime.LocalDateTime);
var nextDate = date.AddDays(1);
KeystrokeEntry[] originals = Enumerable.Range(0, 3)
    .Select(i => new KeystrokeEntry(localTime.AddSeconds(i), "private-application-name", "KeyA", Secret + i, i > 0)).ToArray();
using (var vault = VaultStore.Create(baseline, Password))
{
    foreach (var entry in originals) vault.Append([entry]);
    vault.Append([new KeystrokeEntry(localTime.AddDays(1), "other-app", "KeyB", "next day", false)]);
}

var failures = new List<string>();
int passed = 0;
try
{
    HistoryUnlockSessionTests.Verify(Run);
    Run("text transcript preserves Unicode, whitespace, and repeated input", () =>
    {
        KeystrokeEntry[] entries =
        [
            TranscriptEntry("H", "H"), TranscriptEntry("E", "é"), TranscriptEntry("A", "α🙂"),
            TranscriptEntry("Space"), TranscriptEntry("X", "x"), TranscriptEntry("X", "x", repeat: true),
            TranscriptEntry("Enter"), TranscriptEntry("Tab"), TranscriptEntry("A", "A"),
            TranscriptEntry("Num Enter")
        ];
        Assert(HistoryTextFormatter.Format(entries) == TranscriptHeader() + "Héα🙂 xx" +
            Environment.NewLine + "\tA" + Environment.NewLine);
    });
    Run("text transcript keeps edits and shortcuts visible without applying them", () =>
    {
        KeystrokeEntry[] entries =
        [
            TranscriptEntry("A", "abc"), TranscriptEntry("Backspace"), TranscriptEntry("Backspace", repeat: true),
            TranscriptEntry("Delete"), TranscriptEntry("Left"), TranscriptEntry("Ctrl+S", "s"),
            TranscriptEntry("Alt+F", "f"), TranscriptEntry("Win+R", "r"),
            TranscriptEntry("Ctrl+Alt+Win+D", "d"), TranscriptEntry("Shift+Tab"),
            TranscriptEntry("Shift+Enter"), TranscriptEntry("F1", "\u0001")
        ];
        Assert(HistoryTextFormatter.Format(entries) == TranscriptHeader() +
            "abc[Backspace][Backspace][Delete][Left][Ctrl+S][Alt+F][Win+R][Ctrl+Alt+Win+D][Shift+Tab][Shift+Enter][F1]");
    });
    Run("text transcript hides modifier-only presses but preserves shifted and AltGr text", () =>
    {
        string[] modifiers = ["Left Shift", "Right Shift", "Left Ctrl", "Right Ctrl", "Left Alt", "Right Alt", "Left Win", "Right Win", "Ctrl+Right Alt"];
        var entries = modifiers.Select(key => TranscriptEntry(key)).Concat(
        [
            TranscriptEntry("Shift+A", "A"), TranscriptEntry("Ctrl+Alt+E", "€"),
            TranscriptEntry("Ctrl+Alt+Q"), TranscriptEntry("Shift+Space", " ")
        ]);
        Assert(HistoryTextFormatter.Format(entries) == TranscriptHeader() + "A€[Ctrl+Alt+Q] ");
        Assert(HistoryTextFormatter.Format(modifiers.Select(key => TranscriptEntry(key))) == "");
        Assert(HistoryTextFormatter.Format([]) == "");
    });
    Run("text transcript separates application changes and exact five-minute idle gaps", () =>
    {
        KeystrokeEntry[] entries =
        [
            TranscriptEntry("A", "one"),
            TranscriptEntry("B", "two", seconds: 1, app: "browser"),
            TranscriptEntry("C", "three", seconds: 2),
            TranscriptEntry("D", "four", seconds: 301),
            TranscriptEntry("E", "five", seconds: 601)
        ];
        string separation = Environment.NewLine + Environment.NewLine;
        Assert(HistoryTextFormatter.Format(entries) == TranscriptHeader() + "one" + separation +
            TranscriptHeader(1, "browser") + "two" + separation + TranscriptHeader(2) + "threefour" +
            separation + TranscriptHeader(601) + "five");
    });
    Run("text transcript boundaries survive hidden modifiers and use local timestamps", () =>
    {
        KeystrokeEntry[] entries =
        [
            TranscriptEntry("A", "one") with { Timestamp = localTime.ToOffset(TimeSpan.FromHours(-12)) },
            TranscriptEntry("Left Ctrl", seconds: 300, app: "browser"),
            TranscriptEntry("B", "two", seconds: 301)
        ];
        Assert(HistoryTextFormatter.Format(entries) == TranscriptHeader() + "one" +
            Environment.NewLine + Environment.NewLine + TranscriptHeader(301) + "two");
    });
    int previewIndex = Array.IndexOf(args, "--pure-text-preview");
    PureTextCases.Verify(Run, previewIndex >= 0 && previewIndex + 1 < args.Length ? args[previewIndex + 1] : null);
    Run("pure text handles a long transcript and bounds local undo history", () =>
    {
        var entries = Enumerable.Range(0, 50000).Select(_ => TranscriptEntry("A", "a"))
            .Concat(Enumerable.Range(0, 600).Select(_ => TranscriptEntry("Ctrl+Z")));
        Assert(HistoryTextFormatter.Format(entries, pureText: true) == TranscriptHeader() + new string('a', 50000 - 512));
    });
    Run("password unlock and exact round trip", () =>
    {
        VaultStore.VerifyPassword(baseline, Password);
        Equal(originals, VaultStore.ReadHistory(baseline, Password, date));
        Assert(VaultStore.ReadHistory(baseline, Password, date.AddDays(10)).Count == 0);
    });
    Run("wrong password and empty password fail", () =>
    {
        Throws<CryptographicException>(() => VaultStore.VerifyPassword(baseline, "wrong password"));
        Throws<CryptographicException>(() => VaultStore.ReadHistory(baseline, "", date));
    });
    Run("no plaintext history or password on disk", () =>
    {
        foreach (string path in Directory.GetFiles(baseline))
        {
            string bytes = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            foreach (string secret in new[] { Password, Secret, "private-application-name", "KeyA" })
                Assert(!bytes.Contains(secret, StringComparison.Ordinal), "Plaintext leaked to " + path);
        }
        var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(baseline, "vault.json")))!;
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(metadata["publicKey"]!.GetValue<string>()), out _);
        Throws<CryptographicException>(() => rsa.ExportPkcs8PrivateKey());
    });
    Run("date listing and nonlocal timestamp partition", () =>
    {
        string folder = CopyFixture("dates");
        var offsetEntry = originals[0] with { Timestamp = localTime.ToOffset(TimeSpan.FromHours(-12)), Text = "offset" };
        using (var vault = VaultStore.OpenForRecording(folder)) vault.Append([offsetEntry]);
        File.WriteAllText(Path.Combine(folder, "invalid.tmh"), "ignored");
        Assert(VaultStore.ListDates(folder).SequenceEqual(new[] { nextDate, date }));
        Assert(VaultStore.ReadHistory(folder, Password, date).Last() == offsetEntry);
    });
    Run("password-free recorder and password rotation preserve history", () =>
    {
        string folder = CopyFixture("rotate");
        using var recorder = VaultStore.OpenForRecording(folder);
        recorder.Append([originals[0] with { Text = "before rotation" }]);
        VaultStore.ChangePassword(folder, Password, NewPassword);
        recorder.Append([originals[0] with { Text = "after rotation" }]);
        Throws<CryptographicException>(() => VaultStore.VerifyPassword(folder, Password));
        var history = VaultStore.ReadHistory(folder, NewPassword, date);
        Assert(history.Count == 5 && history[^1].Text == "after rotation");
        Assert(Directory.GetFiles(folder, "*.tmp").Length == 0);
    });
    Run("failed changes and duplicate creation preserve metadata", () =>
    {
        string folder = CopyFixture("atomic");
        byte[] before = File.ReadAllBytes(Path.Combine(folder, "vault.json"));
        Throws<CryptographicException>(() => VaultStore.ChangePassword(folder, "incorrect", NewPassword));
        Throws<ArgumentException>(() => VaultStore.ChangePassword(folder, Password, "short"));
        Throws<IOException>(() => VaultStore.Create(folder, NewPassword));
        Assert(before.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "vault.json"))));
    });
    Run("ciphertext tamper is rejected by reader and recorder", () =>
    {
        string folder = CopyFixture("tamper");
        string path = HistoryPath(folder);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^40] ^= 0x80;
        File.WriteAllBytes(path, bytes);
        Throws<InvalidDataException>(() => VaultStore.ReadHistory(folder, Password, date));
        using var recorder = VaultStore.OpenForRecording(folder);
        Throws<InvalidDataException>(() => recorder.Append([originals[0]]));
        Assert(File.ReadAllBytes(path).SequenceEqual(bytes), "Recorder changed a corrupt file.");
    });
    Run("authenticated encryption rejects tamper with recomputed checksum", () =>
    {
        string folder = CopyFixture("aead");
        string path = HistoryPath(folder);
        byte[] bytes = File.ReadAllBytes(path);
        int start = LastRecordStart(bytes);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(start));
        bytes[start + 4 + 436 + 16] ^= 1;
        RewriteChecksum(bytes, start, length);
        File.WriteAllBytes(path, bytes);
        Throws<CryptographicException>(() => VaultStore.ReadHistory(folder, Password, date));
    });
    Run("record reorder and date header tamper are rejected", () =>
    {
        string folder = CopyFixture("ordering");
        string path = HistoryPath(folder);
        byte[] bytes = File.ReadAllBytes(path);
        int firstLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(44)) + 4;
        int secondLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(44 + firstLength)) + 4;
        byte[] reordered = bytes[..44].Concat(bytes.AsSpan(44 + firstLength, secondLength).ToArray())
            .Concat(bytes.AsSpan(44, firstLength).ToArray()).Concat(bytes[(44 + firstLength + secondLength)..]).ToArray();
        File.WriteAllBytes(path, reordered);
        Throws<InvalidDataException>(() => VaultStore.ReadHistory(folder, Password, date));
        bytes[8] ^= 1;
        File.WriteAllBytes(path, bytes);
        Throws<InvalidDataException>(() => VaultStore.ReadHistory(folder, Password, date));
    });
    Run("truncated record is ignored and recovered before appending", () =>
    {
        string folder = CopyFixture("truncated");
        string path = HistoryPath(folder);
        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^9]);
        Equal(originals.Take(2), VaultStore.ReadHistory(folder, Password, date));
        using (var recorder = VaultStore.OpenForRecording(folder)) recorder.Append([originals[2]]);
        Equal(originals, VaultStore.ReadHistory(folder, Password, date));
    });
    Run("partial length prefixes recover on restart and on cached writer", () =>
    {
        foreach (int count in new[] { 1, 2, 3 })
        {
            string folder = CopyFixture("prefix" + count);
            using var recorder = VaultStore.OpenForRecording(folder);
            recorder.Append([originals[0]]);
            using (var file = File.Open(HistoryPath(folder), FileMode.Append)) file.Write(new byte[count]);
            recorder.Append([originals[1]]);
            Assert(VaultStore.ReadHistory(folder, Password, date).Count == 5);
        }
    });
    Run("partial file headers recover safely", () =>
    {
        string folder = CopyFixture("partialheader");
        string path = HistoryPath(folder);
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..20]);
        Assert(VaultStore.ReadHistory(folder, Password, date).Count == 0);
        using (var recorder = VaultStore.OpenForRecording(folder)) recorder.Append([originals[0]]);
        Equal(originals.Take(1), VaultStore.ReadHistory(folder, Password, date));
    });
    Run("oversized frame length is rejected without truncation", () =>
    {
        string folder = CopyFixture("oversize");
        byte[] bytes = File.ReadAllBytes(HistoryPath(folder));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(44), int.MaxValue);
        File.WriteAllBytes(HistoryPath(folder), bytes);
        Throws<InvalidDataException>(() => VaultStore.ReadHistory(folder, Password, date));
        using var recorder = VaultStore.OpenForRecording(folder);
        Throws<InvalidDataException>(() => recorder.Append([originals[0]]));
        Assert(File.ReadAllBytes(HistoryPath(folder)).SequenceEqual(bytes));
    });
    Run("large valid entries split into bounded batches", () =>
    {
        string folder = CopyFixture("batching");
        var many = Enumerable.Range(0, 260).Select(i => originals[0] with
            { Text = new string('\u0001', 4000) + i }).ToArray();
        using (var recorder = VaultStore.OpenForRecording(folder)) recorder.Append(many);
        Equal(originals.Concat(many), VaultStore.ReadHistory(folder, Password, date));
    });
    Run("invalid entry rejects entire append and disposed recorder fails", () =>
    {
        string folder = CopyFixture("invalid-entry");
        var recorder = VaultStore.OpenForRecording(folder);
        Throws<ArgumentException>(() => recorder.Append([originals[0], originals[1] with { Text = new string('x', 4097) }]));
        Equal(originals, VaultStore.ReadHistory(folder, Password, date));
        recorder.Dispose();
        Throws<ObjectDisposedException>(() => recorder.Append([originals[0]]));
    });
    Run("metadata fields, duplicate fields, and sizes are validated", () =>
    {
        Action<JsonNode>[] corruptions =
        [
            node => node["version"] = 2,
            node => node["iterations"] = 0,
            node => node["iterations"] = int.MaxValue,
            node => node["salt"] = "not base64",
            node => node["nonce"] = Convert.ToBase64String(new byte[11]),
            node => node["tag"] = null,
            node => node["publicKey"] = Convert.ToBase64String(new byte[400]),
            node => node["encryptedPrivateKey"] = "",
            node => node["unexpected"] = true
        ];
        for (int index = 0; index < corruptions.Length; index++)
        {
            string folder = CopyFixture("metadata" + index);
            string path = Path.Combine(folder, "vault.json");
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            corruptions[index](node);
            File.WriteAllText(path, node.ToJsonString());
            Throws<InvalidDataException>(() => VaultStore.OpenForRecording(folder));
        }
        string duplicate = CopyFixture("duplicate");
        string duplicatePath = Path.Combine(duplicate, "vault.json");
        File.WriteAllText(duplicatePath, File.ReadAllText(duplicatePath).Insert(1, "\"version\":1,"));
        Throws<InvalidDataException>(() => VaultStore.OpenForRecording(duplicate));
        File.WriteAllBytes(duplicatePath, new byte[32769]);
        Throws<InvalidDataException>(() => VaultStore.OpenForRecording(duplicate));
    });
    Run("encrypted private key authentication and missing metadata safety", () =>
    {
        string folder = CopyFixture("private-key");
        string path = Path.Combine(folder, "vault.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        byte[] privateKey = Convert.FromBase64String(node["encryptedPrivateKey"]!.GetValue<string>());
        privateKey[10] ^= 1;
        node["encryptedPrivateKey"] = Convert.ToBase64String(privateKey);
        File.WriteAllText(path, node.ToJsonString());
        Throws<CryptographicException>(() => VaultStore.VerifyPassword(folder, Password));
        // The recorder has no need or ability to decrypt the private key.
        using (var recorder = VaultStore.OpenForRecording(folder)) recorder.Append([originals[0]]);
        File.Delete(path);
        Throws<IOException>(() => VaultStore.Create(folder, NewPassword));
    });
}
finally
{
    // root is a freshly created test-only directory with a known absolute temp path.
    Directory.Delete(root, recursive: true);
}

Console.WriteLine($"{passed} passed; {failures.Count} failed.");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures.Add(name); Console.Error.WriteLine("FAIL " + name + "\n" + exception); }
}
KeystrokeEntry TranscriptEntry(string key, string? text = null, int seconds = 0, string app = "notepad", bool repeat = false) =>
    new(localTime.AddSeconds(seconds), app, key, text, repeat);
string TranscriptHeader(int seconds = 0, string app = "notepad") =>
    "[" + localTime.AddSeconds(seconds).ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
    " · " + app + "]" + Environment.NewLine;
string CopyFixture(string name)
{
    string target = Path.Combine(root, name);
    Directory.CreateDirectory(target);
    foreach (string source in Directory.GetFiles(baseline)) File.Copy(source, Path.Combine(target, Path.GetFileName(source)));
    return target;
}
string HistoryPath(string folder) => Path.Combine(folder, date.ToString("yyyy-MM-dd") + ".tmh");
static void Assert(bool condition, string message = "Assertion failed.")
{
    if (!condition) throw new Exception(message);
}
static void Equal(IEnumerable<KeystrokeEntry> expected, IEnumerable<KeystrokeEntry> actual) =>
    Assert(expected.SequenceEqual(actual), "History differs from the original entries.");
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name + ".");
}
static int LastRecordStart(byte[] bytes)
{
    int current = 44;
    while (current + 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(current)) < bytes.Length)
        current += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(current));
    return current;
}
static void RewriteChecksum(byte[] bytes, int start, int frameLength)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    hash.AppendData(bytes.AsSpan(0, 44));
    hash.AppendData(bytes.AsSpan(start, 4 + frameLength - 32));
    hash.GetHashAndReset().CopyTo(bytes, start + 4 + frameLength - 32);
}
