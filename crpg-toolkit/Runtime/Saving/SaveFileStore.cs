using System;
using System.Collections.Generic;
using System.IO;
using CRPG.Saving.Migrations;

namespace CRPG.Saving
{
    /// <summary>Thin wrapper over the exact System.IO calls SaveFileStore needs -- injectable purely so a test
    /// can simulate a failure at a specific step (e.g. "the replace succeeds writing the temp file but throws
    /// during the atomic replace") that a real filesystem can't be reliably forced into on demand (Task 3.5,
    /// responding to "an invalid-path test does not prove failure safety when replacing the same existing
    /// target").</summary>
    public interface ISaveFileIO
    {
        bool FileExists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);

        /// <summary>Atomic on a real filesystem: destinationFileName is replaced by sourceFileName, and
        /// whatever it exactly contained before is renamed to destinationBackupFileName in the same operation.
        /// Requires destinationFileName to already exist.</summary>
        void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName);

        /// <summary>Used only when destinationFileName does NOT already exist (so Replace isn't applicable) --
        /// implemented as copy-then-delete rather than relying on a specific File.Move overload's availability.</summary>
        void Move(string sourceFileName, string destinationFileName);
    }

    public sealed class SystemFileIO : ISaveFileIO
    {
        public bool FileExists(string path) => File.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
        public void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName) =>
            File.Replace(sourceFileName, destinationFileName, destinationBackupFileName);

        public void Move(string sourceFileName, string destinationFileName)
        {
            File.Copy(sourceFileName, destinationFileName, overwrite: true);
            File.Delete(sourceFileName);
        }
    }

    public enum SaveLoadOutcome
    {
        Success,
        FileNotFound,
        IoError,
        Corrupt,
        UnsupportedNewerVersion,
        NoMigrationPath,
    }

    public sealed class SaveLoadResult
    {
        private SaveLoadResult(SaveLoadOutcome outcome, SaveData data, string message)
        {
            Outcome = outcome;
            Data = data;
            Message = message;
        }

        public SaveLoadOutcome Outcome { get; }
        public SaveData Data { get; }
        public string Message { get; }

        public static SaveLoadResult Ok(SaveData data) => new(SaveLoadOutcome.Success, data, null);
        public static SaveLoadResult Fail(SaveLoadOutcome outcome, string message) => new(outcome, null, message);
    }

    public sealed class SaveWriteResult
    {
        private SaveWriteResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public bool Success { get; }
        public string Message { get; }

        public static SaveWriteResult Ok() => new(true, null);
        public static SaveWriteResult Fail(string message) => new(false, message);
    }

    /// <summary>
    /// Task 3.5: atomic write with last-known-good backup, and version/corruption-aware load. Not a
    /// long-lived collaborator with mutable state beyond its constructor-injected io/migrations -- safe to
    /// construct fresh wherever it's needed, same rationale as SaveService's own statelessness.
    /// </summary>
    public sealed class SaveFileStore
    {
        private readonly ISaveFileIO io;
        private readonly IReadOnlyList<ISaveMigration> migrations;

        public SaveFileStore(ISaveFileIO io = null, IReadOnlyList<ISaveMigration> migrations = null)
        {
            this.io = io ?? new SystemFileIO();
            this.migrations = migrations ?? new ISaveMigration[] { new V1ToV2Migration() };
        }

        /// <summary>Writes to path + ".tmp" first, then atomically replaces path via ISaveFileIO.Replace
        /// (one OS-level call producing the ".bak" backup in the same operation) -- a failure during the temp
        /// write never touches path or its backup at all, by construction. If path already exists but is
        /// itself unreadable/corrupt, it is left in place rather than rotated into ".bak" (which would destroy
        /// a possibly-still-good older backup) -- the new save replaces it directly instead.</summary>
        public SaveWriteResult Save(string path, string json)
        {
            var tempPath = path + ".tmp";
            try
            {
                io.WriteAllText(tempPath, json);
            }
            catch (Exception ex)
            {
                return SaveWriteResult.Fail($"Failed writing temp file: {ex.Message}");
            }

            try
            {
                if (!io.FileExists(path))
                {
                    io.Move(tempPath, path);
                }
                else if (IsWellFormed(path))
                {
                    io.Replace(tempPath, path, path + ".bak");
                }
                else
                {
                    io.Move(tempPath, path);
                }

                return SaveWriteResult.Ok();
            }
            catch (Exception ex)
            {
                return SaveWriteResult.Fail($"Failed committing save: {ex.Message}");
            }
        }

        public SaveLoadResult Load(string path)
        {
            if (!io.FileExists(path)) return SaveLoadResult.Fail(SaveLoadOutcome.FileNotFound, $"No save file at '{path}'.");

            string json;
            try
            {
                json = io.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return SaveLoadResult.Fail(SaveLoadOutcome.IoError, ex.Message);
            }

            return LoadFromJson(json);
        }

        /// <summary>Explicit escape hatch for a caller (SessionLoadCoordinator, or eventually a UI) to offer
        /// "load from backup instead" after a primary Load fails -- never invoked automatically by Load itself
        /// ("malformed/newer unsupported data returns a user-facing error," not a silent fallback).</summary>
        public SaveLoadResult LoadBackup(string path) => Load(path + ".bak");

        private bool IsWellFormed(string path)
        {
            try
            {
                return LoadFromJson(io.ReadAllText(path)).Outcome == SaveLoadOutcome.Success;
            }
            catch
            {
                return false;
            }
        }

        private SaveLoadResult LoadFromJson(string json)
        {
            int headerVersion;
            try
            {
                headerVersion = SaveMigrationRunner.ReadFormatVersion(json);
            }
            catch (Exception ex)
            {
                return SaveLoadResult.Fail(SaveLoadOutcome.Corrupt, ex.Message);
            }

            if (headerVersion > SaveData.CurrentFormatVersion)
                return SaveLoadResult.Fail(SaveLoadOutcome.UnsupportedNewerVersion, $"Save is format version {headerVersion}; this version only supports up to {SaveData.CurrentFormatVersion}.");

            string migrated;
            try
            {
                migrated = SaveMigrationRunner.Apply(json, migrations);
            }
            catch (Exception ex)
            {
                return SaveLoadResult.Fail(SaveLoadOutcome.Corrupt, ex.Message);
            }

            var finalVersion = SaveMigrationRunner.ReadFormatVersion(migrated);
            if (finalVersion != SaveData.CurrentFormatVersion)
            {
                return SaveLoadResult.Fail(SaveLoadOutcome.NoMigrationPath,
                    $"Save is stuck at format version {finalVersion} -- no registered migration reaches {SaveData.CurrentFormatVersion}.");
            }

            SaveData data;
            try
            {
                data = SaveService.FromJson(migrated);
            }
            catch (Exception ex)
            {
                return SaveLoadResult.Fail(SaveLoadOutcome.Corrupt, ex.Message);
            }

            if (data?.Header == null) return SaveLoadResult.Fail(SaveLoadOutcome.Corrupt, "Save data has no header.");

            return SaveLoadResult.Ok(data);
        }
    }
}
