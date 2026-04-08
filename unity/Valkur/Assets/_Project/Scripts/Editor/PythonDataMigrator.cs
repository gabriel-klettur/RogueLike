using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Severity level for migration report entries.
    /// </summary>
    public enum MigrationSeverity { Ok, Warning, Error }

    /// <summary>
    /// Single entry in a migration report.
    /// </summary>
    public struct MigrationEntry
    {
        public MigrationSeverity Severity;
        public string Source;
        public string EntityKey;
        public string Message;

        public MigrationEntry(MigrationSeverity severity, string source, string entityKey, string message)
        {
            Severity = severity;
            Source = source;
            EntityKey = entityKey;
            Message = message;
        }

        public override string ToString()
        {
            string tag = Severity switch
            {
                MigrationSeverity.Ok => "OK",
                MigrationSeverity.Warning => "WARN",
                MigrationSeverity.Error => "ERROR",
                _ => "?"
            };
            return $"[{tag}] {Source} / {EntityKey}: {Message}";
        }
    }

    /// <summary>
    /// Accumulates migration results per file/entity and prints a summary report.
    /// Used by PythonDataMigrator for both live imports and dry-run validation.
    /// </summary>
    public class MigrationReport
    {
        private readonly List<MigrationEntry> _entries = new List<MigrationEntry>();

        public int OkCount { get; private set; }
        public int WarningCount { get; private set; }
        public int ErrorCount { get; private set; }
        public int TotalCount => _entries.Count;
        public IReadOnlyList<MigrationEntry> Entries => _entries;

        public void AddOk(string source, string entityKey, string message = "Imported successfully")
        {
            _entries.Add(new MigrationEntry(MigrationSeverity.Ok, source, entityKey, message));
            OkCount++;
        }

        public void AddWarning(string source, string entityKey, string message)
        {
            _entries.Add(new MigrationEntry(MigrationSeverity.Warning, source, entityKey, message));
            WarningCount++;
        }

        public void AddError(string source, string entityKey, string message)
        {
            _entries.Add(new MigrationEntry(MigrationSeverity.Error, source, entityKey, message));
            ErrorCount++;
        }

        /// <summary>
        /// Prints full report to Unity console with summary header.
        /// </summary>
        public void PrintToConsole(string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Migration Report: {title} ===");
            sb.AppendLine($"Total: {TotalCount} | OK: {OkCount} | Warnings: {WarningCount} | Errors: {ErrorCount}");
            sb.AppendLine("---");

            foreach (var entry in _entries)
            {
                sb.AppendLine(entry.ToString());
            }

            sb.AppendLine("=== End Report ===");

            if (ErrorCount > 0)
                Debug.LogError(sb.ToString());
            else if (WarningCount > 0)
                Debug.LogWarning(sb.ToString());
            else
                Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Merge another report into this one.
        /// </summary>
        public void Merge(MigrationReport other)
        {
            _entries.AddRange(other._entries);
            OkCount += other.OkCount;
            WarningCount += other.WarningCount;
            ErrorCount += other.ErrorCount;
        }
    }

    /// <summary>
    /// Editor tool that imports Python JSON data files into Unity ScriptableObjects.
    /// Menu: Valkur > Migration > Import Python Data
    /// Supports dry-run mode (validate without writing) and conversion reports.
    /// </summary>
    public static partial class PythonDataMigrator
    {
        private const string PYTHON_DATA_ROOT = "../../../python/data";
        private const string SO_OUTPUT_ROOT = "Assets/_Project/Data/Catalogs";
    }
}
