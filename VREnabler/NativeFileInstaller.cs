using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace VRPatcher
{
    // Pure BCL: also compiled by tests/runtime_safety/run.ps1 without Unity or game files.
    internal sealed class NativeFileInstaller
    {
        internal static readonly string[] AllowedPaths = {
            "UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json",
            "Plugins/x86_64/UnityOpenXR.dll", "Plugins/x86_64/openxr_loader.dll"
        };
        private readonly string root;
        private readonly string stateDirectory;
        private readonly string ledgerPath;
        private readonly Action<string> log;

        internal NativeFileInstaller(string gameDataDirectory, Action<string> log)
        {
            root = Path.GetFullPath(gameDataDirectory);
            stateDirectory = Path.Combine(root, ".vrmod-native");
            ledgerPath = Path.Combine(stateDirectory, "ledger.json");
            this.log = log;
        }

        internal void Install(IDictionary<string, byte[]> payloads)
        {
            if (payloads.Count != AllowedPaths.Length || AllowedPaths.Any(path => !payloads.ContainsKey(path)))
                throw new InvalidDataException("Expected exactly the three VR native dependency paths.");
            SafePath(".vrmod-native");
            Directory.CreateDirectory(stateDirectory);
            SafePath(".vrmod-native/install.lock");
            // File sharing is enforced by the Windows/Proton runtime. A stale empty lock file
            // is harmless after process exit; the OS releases its handle automatically.
            using (var installLock = new FileStream(Path.Combine(stateDirectory, "install.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                NativeLedger ledger = LoadLedger();
                foreach (string relativePath in AllowedPaths) SafePath(relativePath);
                if (ledger.Disabled)
                {
                    log("Native installation disabled by restore-native.py; retain the ledger/backups and consult the restore instructions before reinstalling.");
                    return;
                }
                // Detect external changes in ALL tracked files before starting another write.
                foreach (NativeEntry entry in ledger.Files)
                    ValidateCurrent(entry, SafePath(entry.Path));
                foreach (string relativePath in AllowedPaths)
                    InstallOne(ledger, relativePath, payloads[relativePath]);
            }
        }

        private void InstallOne(NativeLedger ledger, string relativePath, byte[] payload)
        {
            string destination = SafePath(relativePath);
            string desiredHash = Hash(payload);
            string currentHash = FileHash(destination);
            NativeEntry entry = ledger.Files.SingleOrDefault(item => item.Path == relativePath);
            if (entry == null)
            {
                entry = new NativeEntry { Path = relativePath, OriginalSha256 = currentHash,
                    InstalledSha256 = currentHash, Backup = Array.IndexOf(AllowedPaths, relativePath) + ".original" };
                if (currentHash != null)
                {
                    string backupPath = SafePath(".vrmod-native/" + entry.Backup);
                    // A crash can leave a backup before the ledger. Reuse it only when its
                    // content is exactly the file we are about to replace.
                    if (File.Exists(backupPath))
                    {
                        if (FileHash(backupPath) != currentHash)
                            throw new IOException("Orphan backup differs from current file: " + relativePath);
                    }
                    else
                        AtomicWrite(backupPath, File.ReadAllBytes(destination), null);
                    if (FileHash(backupPath) != currentHash)
                        throw new IOException("Original changed while backing up: " + relativePath);
                }
                ledger.Files.Add(entry);
                // Record the original before any game file is changed, including no-op files.
                SaveLedger(ledger);
            }
            ValidateCurrent(entry, destination);
            if (entry.PendingSha256 != null && currentHash == entry.PendingSha256)
            {
                // A previous process replaced the file but did not commit its journal.
                // Commit that recovery BEFORE staging another version: otherwise changing
                // pending B to C would leave actual B unrecognized after a second crash.
                entry.InstalledSha256 = currentHash;
                entry.PendingSha256 = null;
                SaveLedger(ledger);
            }
            if (currentHash == desiredHash)
            {
                entry.InstalledSha256 = desiredHash;
                entry.PendingSha256 = null;
                SaveLedger(ledger);
                log(relativePath + ": unchanged; SHA-256 " + desiredHash);
                return;
            }

            // Persist write intent first. Recovery accepts either side of an interrupted
            // atomic replacement; no partially written game DLL is exposed.
            entry.PendingSha256 = desiredHash;
            SaveLedger(ledger);
            log(relativePath + ": replacing; SHA-256 " + desiredHash);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            SafePath(relativePath);
            AtomicWrite(destination, payload, currentHash);
            entry.InstalledSha256 = desiredHash;
            entry.PendingSha256 = null;
            SaveLedger(ledger);
            log(relativePath + ": installed; SHA-256 " + desiredHash + "; original retained in .vrmod-native.");
        }

        private NativeLedger LoadLedger()
        {
            SafePath(".vrmod-native/ledger.json");
            if (!File.Exists(ledgerPath))
                return new NativeLedger();
            NativeLedger result;
            using (var stream = File.OpenRead(ledgerPath))
                result = (NativeLedger)new DataContractJsonSerializer(typeof(NativeLedger)).ReadObject(stream);
            if (result == null || result.Version != 1 || result.Files == null || result.Files.Count > AllowedPaths.Length)
                throw new InvalidDataException("Invalid VR native ledger; no files were installed.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (NativeEntry entry in result.Files)
            {
                if (entry == null || !AllowedPaths.Contains(entry.Path) || !seen.Add(entry.Path) ||
                    entry.Backup != Array.IndexOf(AllowedPaths, entry.Path) + ".original" ||
                    !ValidHash(entry.OriginalSha256) || !ValidHash(entry.InstalledSha256) || !ValidHash(entry.PendingSha256))
                    throw new InvalidDataException("Unsafe entry in VR native ledger.");
                if (entry.OriginalSha256 != null &&
                    FileHash(SafePath(".vrmod-native/" + entry.Backup)) != entry.OriginalSha256)
                    throw new InvalidDataException("Missing or modified original backup: " + entry.Path);
            }
            return result;
        }

        private static bool ValidHash(string value)
        {
            return value == null || (value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
        }

        private static void ValidateCurrent(NativeEntry entry, string destination)
        {
            string current = FileHash(destination);
            if (current != entry.InstalledSha256 &&
                (entry.PendingSha256 == null || current != entry.PendingSha256))
                throw new IOException("Refusing to overwrite an externally changed native file: " + entry.Path +
                    ". Review .vrmod-native/ledger.json and restore-native.py first.");
        }

        private void SaveLedger(NativeLedger ledger)
        {
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(NativeLedger)).WriteObject(buffer, ledger);
                AtomicWrite(ledgerPath, buffer.ToArray(), FileHash(ledgerPath));
            }
        }

        // Fixed allowlisted paths, plus reparse-point checks, prevent a symlink/junction under
        // the selected game data directory from redirecting backups or replacement writes.
        private string SafePath(string relative)
        {
            string candidate = root;
            foreach (string part in relative.Split('/'))
            {
                if (part == "." || part == ".." || part.Contains("\\") || part.Contains(":"))
                    throw new InvalidDataException("Invalid native dependency path.");
                candidate = Path.Combine(candidate, part);
                try
                {
                    if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Refusing symbolic link or junction: " + candidate);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return candidate;
        }

        private static void WriteNew(string destination, byte[] bytes)
        {
            using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void AtomicWrite(string destination, byte[] bytes, string expectedHash)
        {
            string temporary = destination + ".vrmod-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteNew(temporary, bytes);
                if (FileHash(destination) != expectedHash)
                    throw new IOException("File changed while preparing replacement: " + destination);
                if (expectedHash == null)
                    File.Move(temporary, destination);
                else
                    File.Replace(temporary, destination, null);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal static string FileHash(string path)
        {
            return File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;
        }

        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }

    [DataContract]
    internal sealed class NativeLedger
    {
        [DataMember(Name = "version")] public int Version = 1;
        [DataMember(Name = "disabled")] public bool Disabled;
        [DataMember(Name = "files")] public List<NativeEntry> Files = new List<NativeEntry>();
    }

    [DataContract]
    internal sealed class NativeEntry
    {
        [DataMember(Name = "path")] public string Path;
        [DataMember(Name = "backup")] public string Backup;
        [DataMember(Name = "original_sha256")] public string OriginalSha256;
        [DataMember(Name = "installed_sha256")] public string InstalledSha256;
        [DataMember(Name = "pending_sha256")] public string PendingSha256;
    }
}
