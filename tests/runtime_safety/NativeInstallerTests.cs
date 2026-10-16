using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using VRPatcher;

public static class NativeInstallerTests
{
    private static int passed;
    private static IDictionary<string, byte[]> Payload(string label)
    {
        return NativeFileInstaller.AllowedPaths.ToDictionary(p => p, p => Encoding.UTF8.GetBytes(label + p));
    }
    private static void Check(bool result, string message)
    {
        if (!result) throw new Exception(message);
    }
    private static void Refused(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected unsafe install to be refused");
    }
    private static NativeLedger ReadLedger(string root)
    {
        using (var file = File.OpenRead(Path.Combine(root, ".vrmod-native/ledger.json")))
            return (NativeLedger)new DataContractJsonSerializer(typeof(NativeLedger)).ReadObject(file);
    }
    private static void WriteLedger(string root, NativeLedger ledger)
    {
        using (var file = File.Create(Path.Combine(root, ".vrmod-native/ledger.json")))
            new DataContractJsonSerializer(typeof(NativeLedger)).WriteObject(file, ledger);
    }
    private static void Test(string name, Action<string, NativeFileInstaller> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "riskofvr-native-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            body(root, new NativeFileInstaller(root, message => { }));
            passed++;
            Console.WriteLine("PASS " + name);
        }
        finally { Directory.Delete(root, true); }
    }
    public static int Run()
    {
        passed = 0;
        Test("fresh install and idempotence", (root, installer) => {
            var payload = Payload("one");
            installer.Install(payload);
            var ledger = ReadLedger(root);
            Check(ledger.Files.Count == 3, "all three writes must be tracked");
            var timestamps = NativeFileInstaller.AllowedPaths.Select(p => File.GetLastWriteTimeUtc(Path.Combine(root, p))).ToArray();
            installer.Install(payload);
            for (int i = 0; i < 3; i++) {
                string path = NativeFileInstaller.AllowedPaths[i];
                Check(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(payload[path]), "wrong payload");
                Check(File.GetLastWriteTimeUtc(Path.Combine(root, path)) == timestamps[i], "no-op rewrote game file");
                Check(ledger.Files[i].OriginalSha256 == null, "original should be absent");
            }
        });
        Test("existing files preserved across upgrade", (root, installer) => {
            foreach (string path in NativeFileInstaller.AllowedPaths) {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path)));
                File.WriteAllText(Path.Combine(root, path), "original " + path);
            }
            installer.Install(Payload("one"));
            installer.Install(Payload("two"));
            foreach (var entry in ReadLedger(root).Files)
                Check(File.ReadAllText(Path.Combine(root, ".vrmod-native", entry.Backup)) == "original " + entry.Path, "backup replaced on upgrade");
        });
        Test("external edit preflight prevents any upgrade", (root, installer) => {
            installer.Install(Payload("one"));
            string first = Path.Combine(root, NativeFileInstaller.AllowedPaths[0]);
            var firstHash = NativeFileInstaller.FileHash(first);
            string changed = Path.Combine(root, NativeFileInstaller.AllowedPaths[2]);
            File.WriteAllText(changed, "newer steam file");
            Refused(() => installer.Install(Payload("two")));
            Check(NativeFileInstaller.FileHash(first) == firstHash, "preflight changed unrelated file");
            Check(File.ReadAllText(changed) == "newer steam file", "external file overwritten");
        });
        Test("deleted installed file is not silently recreated", (root, installer) => {
            installer.Install(Payload("one"));
            File.Delete(Path.Combine(root, NativeFileInstaller.AllowedPaths[1]));
            Refused(() => installer.Install(Payload("one")));
        });
        Test("modified original backup blocks upgrade", (root, installer) => {
            string target = Path.Combine(root, NativeFileInstaller.AllowedPaths[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, "original");
            installer.Install(Payload("one"));
            File.WriteAllText(Path.Combine(root, ".vrmod-native/0.original"), "corrupted");
            Refused(() => installer.Install(Payload("two")));
        });
        Test("interrupted replace recovers both journal sides", (root, installer) => {
            installer.Install(Payload("one"));
            var ledger = ReadLedger(root);
            var next = Payload("two");
            ledger.Files[0].PendingSha256 = NativeFileInstaller.Hash(next[ledger.Files[0].Path]);
            WriteLedger(root, ledger);
            // Simulate crash AFTER the atomic write but BEFORE commit of the journal.
            File.WriteAllBytes(Path.Combine(root, ledger.Files[0].Path), next[ledger.Files[0].Path]);
            installer.Install(next);
            Check(ReadLedger(root).Files.All(e => e.PendingSha256 == null), "pending record not completed");
            ledger = ReadLedger(root);
            var third = Payload("three");
            ledger.Files[0].PendingSha256 = NativeFileInstaller.Hash(third[ledger.Files[0].Path]);
            WriteLedger(root, ledger);
            // Simulate crash BEFORE the atomic write.
            installer.Install(third);
            Check(ReadLedger(root).Files.All(e => e.PendingSha256 == null), "unwritten intent not recovered");
        });
        Test("restore marker prevents reinstall", (root, installer) => {
            installer.Install(Payload("one"));
            var ledger = ReadLedger(root);
            ledger.Disabled = true;
            WriteLedger(root, ledger);
            foreach (string path in NativeFileInstaller.AllowedPaths) File.Delete(Path.Combine(root, path));
            installer.Install(Payload("two"));
            Check(NativeFileInstaller.AllowedPaths.All(p => !File.Exists(Path.Combine(root, p))), "disabled patcher reinstalled files");
        });
        Test("interrupted recovery into a newer version retains current hash", (root, installer) => {
            installer.Install(Payload("A"));
            var ledger = ReadLedger(root);
            var second = Payload("B");
            var entry = ledger.Files[0];
            entry.PendingSha256 = NativeFileInstaller.Hash(second[entry.Path]);
            WriteLedger(root, ledger);
            File.WriteAllBytes(Path.Combine(root, entry.Path), second[entry.Path]);
            // B is on disk, with installed=A/pending=B. Interrupt the NEXT version C
            // after its durable intent is logged but before its native replacement.
            var interrupted = new NativeFileInstaller(root, message => {
                if (message.Contains(": replacing;")) throw new IOException("Simulated interruption before replacement");
            });
            var third = Payload("C");
            Refused(() => interrupted.Install(third));
            var recovered = ReadLedger(root).Files[0];
            Check(recovered.InstalledSha256 == NativeFileInstaller.Hash(second[entry.Path]), "recovery lost actual B hash");
            Check(recovered.PendingSha256 == NativeFileInstaller.Hash(third[entry.Path]), "new write intent was not journaled");
            Check(NativeFileInstaller.FileHash(Path.Combine(root, entry.Path)) == recovered.InstalledSha256, "interruption changed native file");
            installer.Install(third);
            Check(ReadLedger(root).Files.All(e => e.PendingSha256 == null), "second recovery did not complete");
        });
        Test("invalid ledger path rejected", (root, installer) => {
            installer.Install(Payload("one"));
            var ledger = ReadLedger(root);
            ledger.Files[0].Path = "../outside.dll";
            WriteLedger(root, ledger);
            Refused(() => installer.Install(Payload("two")));
        });
        Test("symbolic link cannot redirect native writes", (root, installer) => {
            string outside = Path.Combine(Path.GetTempPath(), "riskofvr-link-target-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            try {
                Directory.CreateSymbolicLink(Path.Combine(root, "Plugins"), outside);
                Refused(() => installer.Install(Payload("one")));
                Check(Directory.GetFileSystemEntries(outside).Length == 0, "write escaped game data directory");
            }
            finally { Directory.Delete(outside, true); }
        });
        Test("concurrent installer is refused", (root, installer) => {
            Directory.CreateDirectory(Path.Combine(root, ".vrmod-native"));
            using (var held = new FileStream(Path.Combine(root, ".vrmod-native/install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Refused(() => installer.Install(Payload("one")));
            Check(!File.Exists(Path.Combine(root, NativeFileInstaller.AllowedPaths[0])), "locked installer changed game file");
        });
        return passed;
    }
    public static void CreateRestoreFixture(string root)
    {
        Directory.CreateDirectory(root);
        var original = Path.Combine(root, NativeFileInstaller.AllowedPaths[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(original));
        File.WriteAllText(original, "cross-language original");
        new NativeFileInstaller(root, message => { }).Install(Payload("integration"));
    }
}
