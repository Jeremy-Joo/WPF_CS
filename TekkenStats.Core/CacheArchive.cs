using System.IO.Compression;

namespace TekkenStats.Core;

/// <summary>
/// wavu 캐시를 zip 하나로 내보내고/불러오는 기능. 다른 PC 로 옮길 때 폴더째 복사하는 대신
/// 파일 하나로 주고받게 한다.
///
/// zip 안에는 <c>&lt;식별코드&gt;.json</c> 들만 든다(<see cref="ReplayCache"/> 파일 그대로).
/// 내용은 wavu 원본이라 기계 독립적 — 어느 PC 로 옮겨도 그대로 읽힌다.
/// </summary>
public static class CacheArchive
{
    /// <summary>
    /// 캐시 폴더의 <c>*.json</c> 을 zip 으로 묶는다. (담긴 파일 수, zip 바이트) 반환.
    /// 파일이 없으면 zip 을 만들지 않고 (0,0).
    /// </summary>
    public static (int Files, long Bytes) Export(string cacheDir, string zipPath)
    {
        if (!Directory.Exists(cacheDir)) return (0, 0);
        var files = Directory.GetFiles(cacheDir, "*.json");
        if (files.Length == 0) return (0, 0);

        string? dir = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(zipPath)) File.Delete(zipPath);   // 덮어쓰기

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            foreach (var f in files)
                zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);

        return (files.Length, new FileInfo(zipPath).Length);
    }

    /// <summary>
    /// zip 의 <c>*.json</c> 을 캐시 폴더로 푼다. <b>받은 시각이 더 최신인 쪽을 남긴다</b> —
    /// 이미 있는 항목을 오래된 것으로 덮어써 유효기간이 앞당겨지는 일을 막는다.
    /// (가져온, 더 오래돼서 건너뛴) 반환.
    /// </summary>
    public static (int Imported, int SkippedOlder) Import(string zipPath, string cacheDir)
    {
        Directory.CreateDirectory(cacheDir);
        int imported = 0, skipped = 0;

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

            // zip-slip 방지: 경로 성분을 버리고 파일명만 쓴다.
            string safeName = Path.GetFileName(entry.Name);
            if (string.IsNullOrEmpty(safeName)) continue;
            string target = Path.Combine(cacheDir, safeName);

            string incoming;
            using (var r = new StreamReader(entry.Open())) incoming = r.ReadToEnd();

            if (File.Exists(target))
            {
                var mine = ReplayCache.FetchedAtOf(File.ReadAllText(target));
                var theirs = ReplayCache.FetchedAtOf(incoming);
                // 둘 다 시각을 알고, 가져온 게 더 오래됐으면 건너뛴다.
                if (mine.HasValue && theirs.HasValue && theirs.Value <= mine.Value)
                {
                    skipped++;
                    continue;
                }
            }

            File.WriteAllText(target, incoming);
            imported++;
        }
        return (imported, skipped);
    }
}
