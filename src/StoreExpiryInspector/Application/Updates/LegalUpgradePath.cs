namespace StoreExpiryInspector.Application.Updates;

public sealed record LegalUpgradeRelease(Version Version, Version SourceMinVersion, Version SourceMaxVersion, string SourceMinMigration, string SourceMaxMigration, IReadOnlyList<string> TargetMigrations, bool Trusted);

public static class LegalUpgradePath
{
    // Candidates must already have passed Release/manifest/signature identity checks.
    public static IReadOnlyList<LegalUpgradeRelease>? Find(Version currentVersion, string currentMigration, Version latest, IEnumerable<LegalUpgradeRelease> releases)
    {
        var nodes = releases.Where(item => item.Trusted && item.Version > currentVersion && item.Version <= latest && item.TargetMigrations.Count > 0)
            .OrderBy(item => item.Version).ToArray();
        var queue = new Queue<(Version Version, string Migration, List<LegalUpgradeRelease> Path)>();
        queue.Enqueue((currentVersion, currentMigration, []));
        var seen = new HashSet<(Version, string)> { (currentVersion, currentMigration) };
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var next in nodes.Where(item => item.Version > node.Version && item.SourceMinVersion <= node.Version && item.SourceMaxVersion >= node.Version && string.CompareOrdinal(item.SourceMinMigration, node.Migration) <= 0 && string.CompareOrdinal(item.SourceMaxMigration, node.Migration) >= 0).OrderByDescending(item => item.Version))
            {
                var path = new List<LegalUpgradeRelease>(node.Path) { next };
                if (next.Version == latest) return path;
                var migration = next.TargetMigrations[^1];
                if (seen.Add((next.Version, migration))) queue.Enqueue((next.Version, migration, path));
            }
        }
        return null;
    }
}
