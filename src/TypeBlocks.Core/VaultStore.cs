using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeBlocks.Core;

/// <summary>
/// Local encrypted history. A recorder holds only the public key; the password unlocks
/// the private key used to read history. The password is never needed for recording.
/// </summary>
public sealed class VaultStore : IDisposable
{
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 1024;
    private const int Iterations = 600_000;
    private const int KeySize = 3072;
    private const int WrappedKeySize = KeySize / 8;
    private const int HeaderSize = 44;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HashSize = 32;
    private const int RecordMetadataSize = 8 + HashSize + WrappedKeySize + NonceSize;
    private const int MaxPlaintextSize = 256 * 1024;
    private const int MinFrameSize = RecordMetadataSize + TagSize + HashSize + 2;
    private const int MaxFrameSize = RecordMetadataSize + TagSize + HashSize + MaxPlaintextSize;
    private const int MaxBatchEntries = 128;
    private static readonly byte[] Magic = "TMHIST01"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
    private readonly string _directory;
    private readonly RSA _publicKey;
    private readonly byte[] _keyId;
    private readonly object _gate = new();
    private readonly Dictionary<DateOnly, ScanState> _writeStates = new();
    private bool _disposed;

    private VaultStore(string directory, Metadata metadata)
    {
        _directory = Path.GetFullPath(directory);
        _publicKey = ImportPublicKey(Convert.FromBase64String(metadata.PublicKey));
        _keyId = SHA256.HashData(Convert.FromBase64String(metadata.PublicKey));
    }

    public static bool Exists(string directory) => File.Exists(MetadataPath(directory));

    public static VaultStore Create(string directory, string password)
    {
        ValidatePassword(password);
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        if (Exists(directory))
            throw new IOException("A vault already exists in this folder.");
        if (Directory.EnumerateFiles(directory, "*.tmh").Any())
            throw new IOException("This folder already contains history. Restore its vault.json before continuing.");
        using var rsa = RSA.Create(KeySize);
        byte[] privateKey = rsa.ExportPkcs8PrivateKey();
        try
        {
            var metadata = EncryptPrivateKey(rsa.ExportSubjectPublicKeyInfo(), privateKey, password);
            WriteMetadata(directory, metadata, replace: false);
            return new VaultStore(directory, metadata);
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    public static VaultStore OpenForRecording(string directory) => new(directory, ReadMetadata(directory));

    public static void VerifyPassword(string directory, string password)
    {
        using var key = UnlockPrivateKey(ReadMetadata(directory), password);
    }

    public static void ChangePassword(string directory, string currentPassword, string newPassword)
    {
        ValidatePassword(newPassword);
        var metadata = ReadMetadata(directory);
        using var rsa = UnlockPrivateKey(metadata, currentPassword);
        byte[] privateKey = rsa.ExportPkcs8PrivateKey();
        try
        {
            var replacement = EncryptPrivateKey(Convert.FromBase64String(metadata.PublicKey), privateKey, newPassword, metadata.Recovery);
            WriteMetadata(directory, replacement, replace: true);
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    public static bool HasRecoveryCode(string directory) => Exists(directory) && ReadMetadata(directory).Recovery is not null;

    /// <summary>Replaces any existing recovery code. Save the returned code separately from this vault.</summary>
    public static string CreateRecoveryCode(string directory, string password)
    {
        using var pending = PrepareRecoveryCode(directory, password);
        pending.Activate();
        return pending.Code;
    }

    /// <summary>Prepares a new code without changing this vault. Activate only after the owner saves the code.</summary>
    public static PendingRecoveryCode PrepareRecoveryCode(string directory, string password)
    {
        directory = Path.GetFullPath(directory);
        var metadata = ReadMetadata(directory);
        using var rsa = UnlockPrivateKey(metadata, password);
        byte[] privateKey = rsa.ExportPkcs8PrivateKey();
        byte[] recoveryKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            byte[] publicKey = Convert.FromBase64String(metadata.PublicKey);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] tag = new byte[TagSize];
            byte[] cipher = new byte[privateKey.Length];
            using (var aes = new AesGcm(recoveryKey, TagSize))
                aes.Encrypt(nonce, privateKey, cipher, tag, MakeRecoveryAad(publicKey));
            var recovery = new RecoveryEnvelope(Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
            var replacement = EncryptPrivateKey(publicKey, privateKey, password, recovery);
            string code = FormatRecoveryCode(recoveryKey);
            return new PendingRecoveryCode(code, () =>
            {
                if (ReadMetadata(directory) != metadata)
                    throw new InvalidOperationException("The vault changed while this recovery code was being prepared. Close this dialog and generate a new code.");
                WriteMetadata(directory, replacement, replace: true);
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(recoveryKey);
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    /// <summary>A pending recovery replacement. Disposal cancels an unactivated replacement.</summary>
    public sealed class PendingRecoveryCode : IDisposable
    {
        private readonly object _gate = new();
        private string? _code;
        private Action? _activate;

        internal PendingRecoveryCode(string code, Action activate)
        {
            _code = code;
            _activate = activate;
        }

        public string Code
        {
            get
            {
                lock (_gate)
                {
                    if (_code is null) throw new ObjectDisposedException(nameof(PendingRecoveryCode));
                    return _code;
                }
            }
        }

        /// <summary>Atomically saves the prepared metadata. Repeating a successful activation is a no-op.</summary>
        public void Activate()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_code is null, this);
                _activate?.Invoke();
                _activate = null;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                // Only the display code and encrypted metadata were retained; private-key buffers were already cleared.
                _code = null;
                _activate = null;
            }
        }
    }

    /// <summary>Sets a new password using a previously saved recovery code without changing history keys.</summary>
    public static void RecoverPassword(string directory, string recoveryCode, string newPassword)
    {
        ValidatePassword(newPassword);
        var metadata = ReadMetadata(directory);
        if (metadata.Recovery is null)
            throw new InvalidOperationException("Recovery was not configured for this vault. Its existing password is required.");
        using var rsa = UnlockRecoveryKey(metadata, recoveryCode);
        byte[] privateKey = rsa.ExportPkcs8PrivateKey();
        try
        {
            var replacement = EncryptPrivateKey(Convert.FromBase64String(metadata.PublicKey), privateKey, newPassword, metadata.Recovery);
            WriteMetadata(directory, replacement, replace: true);
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    public void Append(IReadOnlyList<KeystrokeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > 8192)
            throw new ArgumentException("Append accepts at most 8,192 entries at a time.", nameof(entries));
        // Validate the entire call before writing any of it.
        foreach (var entry in entries) ValidateEntry(entry);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var group in entries.GroupBy(e => DateOnly.FromDateTime(e.Timestamp.LocalDateTime)))
            {
                var date = group.Key;
                byte[] header = MakeHeader(date, _keyId);
                using var stream = new FileStream(HistoryPath(_directory, date), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.Read);
                ScanState state;
                if (stream.Length >= HeaderSize && _writeStates.TryGetValue(date, out var cached)
                    && cached.EndPosition == stream.Length)
                {
                    CheckHeader(stream, header);
                    state = cached;
                }
                else
                {
                    state = Scan(stream, header, null);
                    // Only an incomplete final write is discarded. A complete corrupt record fails.
                    stream.SetLength(state.EndPosition);
                    if (state.EndPosition == 0)
                    {
                        stream.Position = 0;
                        stream.Write(header);
                        state = state with { EndPosition = HeaderSize };
                    }
                }
                stream.Position = state.EndPosition;
                foreach (var batch in group.Chunk(MaxBatchEntries))
                    state = WriteBatch(stream, header, state, batch);
                stream.Flush(flushToDisk: true);
                _writeStates[date] = state;
            }
        }
    }

    public static IReadOnlyList<DateOnly> ListDates(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<DateOnly>();
        return Directory.EnumerateFiles(directory, "*.tmh")
            .Select(path => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? (DateOnly?)date : null)
            .Where(date => date.HasValue).Select(date => date!.Value).OrderDescending().ToArray();
    }

    public static IReadOnlyList<KeystrokeEntry> ReadHistory(string directory, string password, DateOnly date)
    {
        var metadata = ReadMetadata(directory);
        using var rsa = UnlockPrivateKey(metadata, password);
        string path = HistoryPath(directory, date);
        if (!File.Exists(path)) return Array.Empty<KeystrokeEntry>();
        byte[] keyId = SHA256.HashData(Convert.FromBase64String(metadata.PublicKey));
        byte[] header = MakeHeader(date, keyId);
        List<KeystrokeEntry> result = [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Scan(stream, header, (prefix, frame) =>
        {
            byte[]? key = null;
            byte[] plaintext = new byte[frame.Length - RecordMetadataSize - TagSize - HashSize];
            try
            {
                key = rsa.Decrypt(frame.AsSpan(8 + HashSize, WrappedKeySize).ToArray(), RSAEncryptionPadding.OaepSHA256);
                if (key.Length != 32) throw new CryptographicException("Invalid history encryption key.");
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(frame.AsSpan(8 + HashSize + WrappedKeySize, NonceSize),
                    frame.AsSpan(RecordMetadataSize + TagSize, plaintext.Length),
                    frame.AsSpan(RecordMetadataSize, TagSize), plaintext,
                    MakeRecordAad(header, prefix, frame.AsSpan(0, RecordMetadataSize)));
                var entries = JsonSerializer.Deserialize<KeystrokeEntry[]>(plaintext, JsonOptions)
                    ?? throw new InvalidDataException("A history record contains no entries.");
                if (entries.Length is < 1 or > MaxBatchEntries)
                    throw new InvalidDataException("A history record has an invalid entry count.");
                foreach (var entry in entries) ValidateEntry(entry);
                result.AddRange(entries);
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("The history could not be authenticated. It may have been modified.", ex);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                throw new InvalidDataException("The history contains an invalid record.", ex);
            }
            finally
            {
                if (key is not null) CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        });
        return result;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _publicKey.Dispose();
            _disposed = true;
        }
    }

    private ScanState WriteBatch(FileStream stream, byte[] header, ScanState state, KeystrokeEntry[] entries)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
        try
        {
            if (plaintext.Length > MaxPlaintextSize)
            {
                if (entries.Length == 1) throw new ArgumentException("The entry is too large to record.");
                int middle = entries.Length / 2;
                state = WriteBatch(stream, header, state, entries[..middle]);
                return WriteBatch(stream, header, state, entries[middle..]);
            }
            byte[] key = RandomNumberGenerator.GetBytes(32);
            try
            {
                byte[] frame = new byte[RecordMetadataSize + TagSize + plaintext.Length + HashSize];
                byte[] prefix = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(prefix, frame.Length);
                BinaryPrimitives.WriteInt64LittleEndian(frame, state.NextSequence);
                state.LastHash.CopyTo(frame, 8);
                _publicKey.Encrypt(key, RSAEncryptionPadding.OaepSHA256).CopyTo(frame, 8 + HashSize);
                RandomNumberGenerator.Fill(frame.AsSpan(8 + HashSize + WrappedKeySize, NonceSize));
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(frame.AsSpan(8 + HashSize + WrappedKeySize, NonceSize), plaintext,
                    frame.AsSpan(RecordMetadataSize + TagSize, plaintext.Length),
                    frame.AsSpan(RecordMetadataSize, TagSize),
                    MakeRecordAad(header, prefix, frame.AsSpan(0, RecordMetadataSize)));
                byte[] hash = HashRecord(header, prefix, frame.AsSpan(0, frame.Length - HashSize));
                hash.CopyTo(frame, frame.Length - HashSize);
                stream.Write(prefix);
                stream.Write(frame);
                return new ScanState(stream.Position, checked(state.NextSequence + 1), hash);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static ScanState Scan(FileStream stream, byte[] header, Action<byte[], byte[]>? consume)
    {
        stream.Position = 0;
        long length = stream.Length;
        if (length < HeaderSize)
        {
            byte[] partialHeader = new byte[(int)length];
            stream.ReadExactly(partialHeader);
            if (!partialHeader.AsSpan().SequenceEqual(header.AsSpan(0, partialHeader.Length)))
                throw new InvalidDataException("The history file header is invalid.");
            return new ScanState(0, 0, new byte[HashSize]);
        }
        CheckHeader(stream, header);
        var state = new ScanState(HeaderSize, 0, new byte[HashSize]);
        while (stream.Position < length)
        {
            if (length - stream.Position < 4) return state;
            byte[] prefix = new byte[4];
            stream.ReadExactly(prefix);
            int frameLength = BinaryPrimitives.ReadInt32LittleEndian(prefix);
            if (frameLength < MinFrameSize || frameLength > MaxFrameSize)
                throw new InvalidDataException("A history record has an invalid size.");
            if (length - stream.Position < frameLength) return state;
            byte[] frame = new byte[frameLength];
            stream.ReadExactly(frame);
            byte[] actualHash = HashRecord(header, prefix, frame.AsSpan(0, frameLength - HashSize));
            if (BinaryPrimitives.ReadInt64LittleEndian(frame) != state.NextSequence
                || !CryptographicOperations.FixedTimeEquals(frame.AsSpan(8, HashSize), state.LastHash)
                || !CryptographicOperations.FixedTimeEquals(frame.AsSpan(frameLength - HashSize), actualHash))
                throw new InvalidDataException("The history is damaged or has been modified.");
            consume?.Invoke(prefix, frame);
            state = new ScanState(stream.Position, checked(state.NextSequence + 1), actualHash);
        }
        return state;
    }

    private static void CheckHeader(FileStream stream, byte[] expected)
    {
        stream.Position = 0;
        byte[] actual = new byte[HeaderSize];
        stream.ReadExactly(actual);
        if (!actual.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("The history header does not match this vault and date.");
    }

    private static byte[] MakeHeader(DateOnly date, byte[] keyId)
    {
        byte[] header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), date.DayNumber);
        keyId.CopyTo(header, 12);
        return header;
    }

    private static byte[] MakeRecordAad(byte[] header, byte[] prefix, ReadOnlySpan<byte> metadata)
    {
        byte[] aad = new byte[header.Length + prefix.Length + metadata.Length];
        header.CopyTo(aad, 0);
        prefix.CopyTo(aad, header.Length);
        metadata.CopyTo(aad.AsSpan(header.Length + prefix.Length));
        return aad;
    }

    private static byte[] HashRecord(byte[] header, byte[] prefix, ReadOnlySpan<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        hash.AppendData(prefix);
        hash.AppendData(body);
        return hash.GetHashAndReset();
    }

    private static void ValidateEntry(KeystrokeEntry? entry)
    {
        if (entry is null || entry.Application is null || entry.Key is null
            || entry.Application.Length > 1024 || entry.Key.Length > 256 || entry.Text?.Length > 4096)
            throw new ArgumentException("An entry contains a missing field or a field that is too long.");
    }

    private static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
            throw new ArgumentException($"Choose a password between {MinimumPasswordLength} and {MaximumPasswordLength} characters.", nameof(password));
    }

    private static Metadata EncryptPrivateKey(byte[] publicKey, byte[] privateKey, string password, RecoveryEnvelope? recovery = null)
    {
        int version = recovery is null ? 1 : 2;
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] cipher = new byte[privateKey.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, privateKey, cipher, tag, MakePrivateKeyAad(version, Iterations, salt, publicKey, recovery));
            return new Metadata(version, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(publicKey),
                Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipher), recovery);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static RSA UnlockPrivateKey(Metadata metadata, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length > MaximumPasswordLength)
            throw new ArgumentException("The password is too long.", nameof(password));
        byte[] salt = Convert.FromBase64String(metadata.Salt);
        byte[] publicKey = Convert.FromBase64String(metadata.PublicKey);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, metadata.Iterations, HashAlgorithmName.SHA256, 32);
        byte[] cipher = Convert.FromBase64String(metadata.EncryptedPrivateKey);
        byte[] privateKey = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(Convert.FromBase64String(metadata.Nonce), cipher, Convert.FromBase64String(metadata.Tag),
                privateKey, MakePrivateKeyAad(metadata.Version, metadata.Iterations, salt, publicKey, metadata.Recovery));
            return ImportMatchingPrivateKey(privateKey, publicKey);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("The password is incorrect, or the vault metadata has been modified.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static RSA UnlockRecoveryKey(Metadata metadata, string recoveryCode)
    {
        byte[] key = ParseRecoveryCode(recoveryCode);
        byte[] publicKey = Convert.FromBase64String(metadata.PublicKey);
        var recovery = metadata.Recovery!;
        byte[] cipher = Convert.FromBase64String(recovery.EncryptedPrivateKey);
        byte[] privateKey = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(Convert.FromBase64String(recovery.Nonce), cipher, Convert.FromBase64String(recovery.Tag),
                privateKey, MakeRecoveryAad(publicKey));
            return ImportMatchingPrivateKey(privateKey, publicKey);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("The recovery code is incorrect, or the vault metadata has been modified.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static RSA ImportMatchingPrivateKey(byte[] privateKey, byte[] publicKey)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
            if (bytesRead != privateKey.Length || rsa.KeySize != KeySize
                || !CryptographicOperations.FixedTimeEquals(rsa.ExportSubjectPublicKeyInfo(), publicKey))
                throw new CryptographicException("The private key does not match the vault.");
            return rsa;
        }
        catch { rsa.Dispose(); throw; }
    }

    private static string FormatRecoveryCode(byte[] key) => string.Create(71, key, static (characters, bytes) =>
    {
        const string hex = "0123456789ABCDEF";
        int position = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (index > 0 && index % 4 == 0) characters[position++] = '-';
            characters[position++] = hex[bytes[index] >> 4];
            characters[position++] = hex[bytes[index] & 15];
        }
    });

    private static byte[] ParseRecoveryCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length > 256) throw new CryptographicException("The recovery code is invalid.");
        byte[] key = new byte[32];
        int digits = 0;
        try
        {
            foreach (char character in code)
            {
                if (character == '-' || char.IsWhiteSpace(character)) continue;
                int value = character switch
                {
                    >= '0' and <= '9' => character - '0',
                    >= 'A' and <= 'F' => character - 'A' + 10,
                    >= 'a' and <= 'f' => character - 'a' + 10,
                    _ => -1
                };
                if (value < 0 || digits >= 64) throw new CryptographicException("The recovery code is invalid.");
                key[digits / 2] |= (byte)(value << (digits % 2 == 0 ? 4 : 0));
                digits++;
            }
            if (digits != 64) throw new CryptographicException("The recovery code is invalid.");
            return key;
        }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    private static byte[] MakePrivateKeyAad(int version, int iterations, byte[] salt, byte[] publicKey, RecoveryEnvelope? recovery)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        // Stable version-one encryption domain: changing this would invalidate existing passwords.
        writer.Write("TypeMemory private key");
        writer.Write(version);
        writer.Write(iterations);
        writer.Write(salt.Length);
        writer.Write(salt);
        writer.Write(publicKey.Length);
        writer.Write(publicKey);
        if (version == 2)
        {
            // Bind the recovery envelope to password authentication as well as its own AES-GCM tag.
            writer.Write(Convert.FromBase64String(recovery!.Nonce));
            writer.Write(Convert.FromBase64String(recovery.Tag));
            byte[] cipher = Convert.FromBase64String(recovery.EncryptedPrivateKey);
            writer.Write(cipher.Length);
            writer.Write(cipher);
        }
        return stream.ToArray();
    }

    private static byte[] MakeRecoveryAad(byte[] publicKey)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("TypeBlocks recovery private key");
        writer.Write(2);
        writer.Write(publicKey.Length);
        writer.Write(publicKey);
        return stream.ToArray();
    }

    private static RSA ImportPublicKey(byte[] bytes)
    {
        RSA rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(bytes, out int bytesRead);
            if (bytesRead != bytes.Length || rsa.KeySize != KeySize)
                throw new InvalidDataException("The vault public key is invalid.");
            return rsa;
        }
        catch { rsa.Dispose(); throw; }
    }

    private static Metadata ReadMetadata(string directory)
    {
        using var stream = new FileStream(MetadataPath(directory), FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        if (stream.Length is < 1 or > 32768) throw new InvalidDataException("The vault metadata size is invalid.");
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The vault metadata is invalid.");
            RejectDuplicateFields(document.RootElement);
            var metadata = document.Deserialize<Metadata>(JsonOptions)
                ?? throw new InvalidDataException("The vault metadata is missing.");
            if (metadata.Version is not (1 or 2) || metadata.Iterations is < Iterations or > 2_000_000
                || DecodeLength(metadata.Salt) != 32 || DecodeLength(metadata.Nonce) != NonceSize
                || DecodeLength(metadata.Tag) != TagSize || DecodeLength(metadata.EncryptedPrivateKey) is < 128 or > 8192
                || DecodeLength(metadata.PublicKey) is < 128 or > 1024)
                throw new InvalidDataException("The vault metadata has unsupported or invalid fields.");
            if ((metadata.Version == 1 && metadata.Recovery is not null)
                || (metadata.Version == 2 && (metadata.Recovery is null
                    || DecodeLength(metadata.Recovery.Nonce) != NonceSize
                    || DecodeLength(metadata.Recovery.Tag) != TagSize
                    || DecodeLength(metadata.Recovery.EncryptedPrivateKey) != DecodeLength(metadata.EncryptedPrivateKey))))
                throw new InvalidDataException("The recovery metadata has unsupported or invalid fields.");
            using var rsa = ImportPublicKey(Convert.FromBase64String(metadata.PublicKey));
            return metadata;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or CryptographicException)
        {
            throw new InvalidDataException("The vault metadata is damaged or invalid.", ex);
        }
    }

    private static void RejectDuplicateFields(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("The vault metadata contains duplicate fields.");
            RejectDuplicateFields(property.Value);
        }
    }

    private static int DecodeLength(string? value) => value is null ? -1 : Convert.FromBase64String(value).Length;

    private static void WriteMetadata(string directory, Metadata metadata, bool replace)
    {
        string path = MetadataPath(directory);
        string temporary = Path.Combine(Path.GetFullPath(directory), $".vault-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, metadata, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (replace) File.Replace(temporary, path, null);
            else File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string MetadataPath(string directory) => Path.Combine(Path.GetFullPath(directory), "vault.json");
    private static string HistoryPath(string directory, DateOnly date) =>
        Path.Combine(Path.GetFullPath(directory), date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".tmh");
    private sealed record ScanState(long EndPosition, long NextSequence, byte[] LastHash);
    private sealed record Metadata(int Version, int Iterations, string Salt, string PublicKey,
        string Nonce, string Tag, string EncryptedPrivateKey,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecoveryEnvelope? Recovery = null);
    private sealed record RecoveryEnvelope(string Nonce, string Tag, string EncryptedPrivateKey);
}
