using System.Globalization;
using System.Text.Json;

var modUpdates = new Dictionary<string, List<DownloadCountUpdate>>(256);
var hunkLines = new List<string>(64);
DateTime commitTime = default;
bool inDatabaseDiff = false;
bool pastFirstHunkHeader = false;

// Stream line-by-line instead of loading the entire file into memory
using (var fs = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
using (var reader = new StreamReader(fs))
{
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        if (line.StartsWith("Date:"))
        {
            commitTime = ParseGitDate(line);
            continue;
        }

        if (line == "+++ b/database.json")
        {
            inDatabaseDiff = true;
            pastFirstHunkHeader = false;
            hunkLines.Clear();
            continue;
        }

        if (!inDatabaseDiff) continue;

        if (!pastFirstHunkHeader)
        {
            if (line.StartsWith("@@"))
            {
                pastFirstHunkHeader = true;
                hunkLines.Clear();
            }
            continue;
        }

        if (line.StartsWith("@@"))
        {
            ProcessHunk(hunkLines, commitTime, modUpdates);
            hunkLines.Clear();
            continue;
        }

        if (line.StartsWith("commit ") || line.StartsWith("diff --git"))
        {
            ProcessHunk(hunkLines, commitTime, modUpdates);
            hunkLines.Clear();
            inDatabaseDiff = false;
            pastFirstHunkHeader = false;
            continue;
        }

        hunkLines.Add(line);
    }

    if (hunkLines.Count > 0)
        ProcessHunk(hunkLines, commitTime, modUpdates);
}

// Merge old repo updates into new repos (copies to avoid shared mutation)
foreach (var (newRepo, oldRepoList) in Offsets.oldRepos)
{
    if (!modUpdates.TryGetValue(newRepo, out var newUpdates))
    {
        newUpdates = [];
        modUpdates[newRepo] = newUpdates;
    }
    foreach (var oldRepo in oldRepoList)
    {
        if (modUpdates.TryGetValue(oldRepo, out var oldUpdates))
        {
            foreach (var u in oldUpdates)
                newUpdates.Add(new DownloadCountUpdate(u.T, u.D));
        }
    }
}

// Apply between-timestamp download count adjustments
foreach (var (repo, betweens) in Offsets.offsetBetween)
{
    if (!modUpdates.TryGetValue(repo, out var updates)) continue;
    foreach (var update in updates)
    {
        foreach (var between in betweens)
        {
            if (update.T > between.AfterUnixTimestamp && update.T <= between.BeforeUnixTimestamp)
                update.D += between.DownloadCount;
        }
    }
}

// Add static offset entries (copies to keep Offsets data immutable)
foreach (var (repo, staticUpdates) in Offsets.offsets)
{
    if (!modUpdates.TryGetValue(repo, out var updates))
    {
        updates = [];
        modUpdates[repo] = updates;
    }
    foreach (var u in staticUpdates)
        updates.Add(new DownloadCountUpdate(u.T, u.D));
}

// Write compact JSON to stdout via Utf8JsonWriter for maximum throughput
using var stdout = Console.OpenStandardOutput();
using var buffered = new BufferedStream(stdout, 1024 * 1024);
using var writer = new Utf8JsonWriter(buffered, new JsonWriterOptions { SkipValidation = true });

writer.WriteStartArray();
foreach (var (repo, updates) in modUpdates)
{
    writer.WriteStartObject();
    var repoName = repo.StartsWith("https://github.com/") ? repo[19..] : repo;
    writer.WriteString("r"u8, repoName);
    writer.WriteStartArray("u"u8);
    foreach (var u in updates)
    {
        writer.WriteStartObject();
        writer.WriteNumber("t"u8, u.T);
        writer.WriteNumber("d"u8, u.D);
        writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteEndObject();
}
writer.WriteEndArray();

// --- Helper methods ---

static void ProcessHunk(List<string> lines, DateTime time, Dictionary<string, List<DownloadCountUpdate>> modUpdates)
{
    if (lines.Count == 0) return;

    string? contextRepo = null, addedRepo = null, removedRepo = null, installerRepo = null;
    bool foundDownloadCount = false;
    int downloadCount = 0;

    foreach (var line in lines)
    {
        if (line.Length == 0) continue;

        if (line.Contains("\"repo\":"))
        {
            var val = ExtractJsonString(line, "\"repo\":");
            if (val != null)
            {
                switch (line[0])
                {
                    case '+': addedRepo ??= val; break;
                    case '-': removedRepo ??= val; break;
                    default: contextRepo ??= val; break;
                }
            }
        }

        if (installerRepo == null && line.Contains("\"installerDownloadUrl\":"))
        {
            var url = ExtractJsonString(line, "\"installerDownloadUrl\":");
            if (url != null)
                installerRepo = TruncateToRepo(url);
        }

        if (!foundDownloadCount && line[0] == '+' && line.Contains("\"downloadCount\":"))
        {
            downloadCount = ExtractInt(line, "\"downloadCount\":");
            foundDownloadCount = true;
        }
    }

    var repo = contextRepo ?? addedRepo ?? removedRepo ?? installerRepo;
    if (repo == null || !foundDownloadCount) return;

    long unixTime = ((DateTimeOffset)DateTime.SpecifyKind(time, DateTimeKind.Utc)).ToUnixTimeSeconds();

    if (!modUpdates.TryGetValue(repo, out var updates))
    {
        updates = [];
        modUpdates[repo] = updates;
    }
    updates.Add(new DownloadCountUpdate(unixTime, downloadCount));
}

static string? ExtractJsonString(string line, string key)
{
    int keyIdx = line.IndexOf(key, StringComparison.Ordinal);
    if (keyIdx < 0) return null;

    int openQuote = line.IndexOf('"', keyIdx + key.Length);
    if (openQuote < 0) return null;

    int closeQuote = line.IndexOf('"', openQuote + 1);
    if (closeQuote < 0) return null;

    return line[(openQuote + 1)..closeQuote];
}

static int ExtractInt(string line, string key)
{
    int keyIdx = line.IndexOf(key, StringComparison.Ordinal);
    if (keyIdx < 0) return 0;

    var span = line.AsSpan()[(keyIdx + key.Length)..].Trim();
    if (span.EndsWith(","))
        span = span[..^1];

    return int.TryParse(span, out int result) ? result : 0;
}

static string TruncateToRepo(string url)
{
    int slashCount = 0;
    for (int i = 0; i < url.Length; i++)
    {
        if (url[i] == '/')
        {
            slashCount++;
            if (slashCount == 5)
                return url[..i];
        }
    }
    return url;
}

static DateTime ParseGitDate(string line)
{
    var span = line.AsSpan();
    int colonIdx = span.IndexOf(':');
    span = span[(colonIdx + 1)..].Trim();
    // e.g. "Tue Mar 29 23:53:02 2022 +0000"

    // Skip day-of-week (3 chars + space)
    span = span[4..];
    // "Mar 29 23:53:02 2022 +0000" or "Mar  5 23:53:02 2022 +0000"

    int month = span[..3] switch
    {
        "Jan" => 1,
        "Feb" => 2,
        "Mar" => 3,
        "Apr" => 4,
        "May" => 5,
        "Jun" => 6,
        "Jul" => 7,
        "Aug" => 8,
        "Sep" => 9,
        "Oct" => 10,
        "Nov" => 11,
        "Dec" => 12,
        _ => 0
    };

    span = span[3..].TrimStart();
    // "29 23:53:02 2022 +0000"

    int spaceIdx = span.IndexOf(' ');
    int day = int.Parse(span[..spaceIdx]);
    span = span[(spaceIdx + 1)..];
    // "23:53:02 2022 +0000"

    int hours = int.Parse(span[..2]);
    int minutes = int.Parse(span[3..5]);
    int seconds = int.Parse(span[6..8]);
    int year = int.Parse(span[9..13]);

    return new DateTime(year, month, day, hours, minutes, seconds, DateTimeKind.Utc);
}