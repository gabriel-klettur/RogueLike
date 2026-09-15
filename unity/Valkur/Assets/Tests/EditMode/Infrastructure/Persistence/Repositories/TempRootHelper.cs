using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Infrastructure.Persistence.Repositories
{
    // ── Helpers to scope a temp StreamingAssets root for JSON-file fixtures ──────

    internal static class TempRootHelper
    {
        public static string Create(string tag)
        {
            string p = Path.Combine(Path.GetTempPath(),
                $"valkur_repo_tests_{tag}_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(p);
            return p;
        }

        public static void Cleanup(string root)
        {
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
