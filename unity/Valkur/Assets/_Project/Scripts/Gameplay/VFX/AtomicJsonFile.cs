using System;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Writes a JSON string to disk atomically so a crash mid-write cannot
    /// corrupt the primary file.
    ///
    /// Strategy:
    ///   1. Write content to <c>path.tmp</c>.
    ///   2. If the target exists, replace it (target → backup, tmp → target).
    ///      On platforms where <see cref="File.Replace"/> is unavailable, fall
    ///      back to a manual copy+delete+rename sequence.
    ///   3. If the target does not yet exist (first save), just move tmp → target.
    ///
    /// Keeps exactly one <c>.bak</c> file alongside the primary.
    /// </summary>
    public static class AtomicJsonFile
    {
        /// <summary>
        /// Write <paramref name="content"/> to <paramref name="path"/> atomically.
        /// Creates all missing parent directories.
        /// Throws on error — callers should wrap in try/catch and mark save as failed.
        /// </summary>
        public static void Write(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (content == null) content = string.Empty;

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tmpPath = path + ".tmp";
            string bakPath = path + ".bak";

            // Write to temp first.
            File.WriteAllText(tmpPath, content, System.Text.Encoding.UTF8);

            if (File.Exists(path))
            {
                try
                {
                    // Atomic replace: target → backup, tmp → target.
                    File.Replace(tmpPath, path, bakPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    // Fallback for platforms that don't support File.Replace.
                    if (File.Exists(bakPath)) File.Delete(bakPath);
                    File.Copy(path, bakPath, overwrite: true);
                    File.Delete(path);
                    File.Move(tmpPath, path);
                }
            }
            else
            {
                // Target doesn't exist yet — just rename tmp to final.
                File.Move(tmpPath, path);
            }
        }
    }
}
