using System.Globalization;
using System.Text.Json;

class Core 
{
	static void Main(string[] args)
	{
		var logFile = File.ReadAllText(args[0]);

		var splits = logFile.Split($"\r\ncommit ");

		List<Commit> commits = new();
		foreach (var split in splits)
		{
			var commit = new Commit(split.Split("\r\n"));
			commits.Add(commit);
		}

		var updates = commits.SelectMany(x => x.Updates);
		var includedMods = updates.Select(x => x.Repo).Distinct();

		List<Entry> entries = new();
		foreach (var mod in includedMods)
		{
			List<DownloadCountUpdate> countUpdates = new();
			foreach (var update in updates)
			{
				if (update.Repo == mod)
				{
					countUpdates.Add(new DownloadCountUpdate()
					{
						DownloadCount = update.DownloadCount,
						UnixTimestamp = ((DateTimeOffset)update.Time).ToUnixTimeSeconds()
					});
				}
			}

			var entry = new Entry()
			{
				Repo = mod,
				Updates = countUpdates.ToArray()
			};

			entries.Add(entry);
		}

		var json = JsonSerializer.Serialize(entries);

		Console.WriteLine(json);
	}

	public static int GetNthIndex(string s, char t, int n)
	{
		var count = 0;
		for (var i = 0; i < s.Length; i++)
		{
			if (s[i] == t)
			{
				count++;
				if (count == n)
				{
					return i;
				}
			}
		}

		return -1;
	}
}

struct Commit
{
	public DateTime Time;
	public string[] Lines;
	public ModDatabaseUpdate[] Updates;

	public Commit(string[] lines)
	{
		var enGB = CultureInfo.InvariantCulture;

		Lines = lines;
		var time = lines.First(x => x.StartsWith("Date:"))[8..32];
		var month = DateTime.ParseExact(time[4..7], "MMM", enGB).Month;
		var dayOfMonth_temp = time[8..10];

		var offset = 0;
		if (dayOfMonth_temp[1] == ' ')
		{
			dayOfMonth_temp = $"0{dayOfMonth_temp[0]}";
			offset = 1;
		}

		var dayOfMonth = DateTime.ParseExact(dayOfMonth_temp, "dd", enGB).Day;
		var clockTime = DateTime.ParseExact(time[(11 - offset)..(19 - offset)], "HH:mm:ss", enGB).TimeOfDay;
		var year = DateTime.ParseExact(time[(20 - offset)..(24 - offset)], "yyyy", enGB).Year;
		Time = new DateTime(year, month, dayOfMonth, clockTime.Hours, clockTime.Minutes, clockTime.Seconds);

		var indexOfStartBlock = Lines.ToList().IndexOf("+++ b/database.json");

		List<ModDatabaseUpdate> updates = new();
		List<string> lineStorage = new();
		foreach (var item in lines[(indexOfStartBlock + 2)..])
		{
			if (item.StartsWith("@@") || item == lines[^1])
			{
				var update = new ModDatabaseUpdate(lineStorage.ToArray(), Time);
				updates.Add(update);
				lineStorage.Clear();
				continue;
			}

			lineStorage.Add(item);
		}

		Updates = updates.ToArray();
	}
}

struct ModDatabaseUpdate
{
	public DateTime Time;
	public string Repo;
	public int DownloadCount;
	public int DownloadCountChange;

	public ModDatabaseUpdate(string[] lines, DateTime time)
	{
		Time = time;

		if (!lines.Any(x => x.StartsWith("       \"repo\":")))
		{
			if (lines.Any(x => x.StartsWith("+      \"repo\":")))
			{
				Repo = lines.First(x => x.StartsWith("+      \"repo\":"))[16..^2];
			}
			else if (lines.Any(x => x.StartsWith("-      \"repo\":")))
			{
				Repo = lines.First(x => x.StartsWith("-      \"repo\":"))[16..^2];
			}
			else if (lines.Any(x => x.StartsWith("     \"installerDownloadUrl\":")))
			{
				var installerDownloadUrl = lines.First(x => x.StartsWith("     \"installerDownloadUrl\":"))[30..^2];

				var indexOf5thSlash = Core.GetNthIndex(installerDownloadUrl, '/', 5);

				Repo = installerDownloadUrl[..indexOf5thSlash];
			}
			else
			{
				// what
				Repo = "IGNORE_ENTRY";
				DownloadCount = 0;
				DownloadCountChange = 0;
				return;
			}
		}
		else
		{
			Repo = lines.First(x => x.StartsWith("       \"repo\":"))[16..^2];
		}

		DownloadCount = 0;
		var removedCount = 0;
		if (lines.Any(x => x.StartsWith("+      \"downloadCount\":")))
		{
			var downloadCount = lines.First(x => x.StartsWith("+      \"downloadCount\":"));
			DownloadCount = downloadCount[^1] == ',' ? int.Parse(downloadCount[24..^1]) : int.Parse(downloadCount[24..]);

			var removedCount_temp = lines.FirstOrDefault(x => x.StartsWith("-      \"downloadCount\":"));

			removedCount = removedCount_temp == null
				? 0
				: removedCount_temp[^1] == ','
					? int.Parse(removedCount_temp[24..^1])
					: int.Parse(removedCount_temp[24..]);
		}
		else if (lines.Any(x => x.StartsWith("+    \"downloadCount\":")))
		{
			var downloadCount = lines.First(x => x.StartsWith("+    \"downloadCount\":"));
			DownloadCount = downloadCount[^1] == ',' ? int.Parse(downloadCount[22..^1]) : int.Parse(downloadCount[22..]);

			var removedCount_temp = lines.FirstOrDefault(x => x.StartsWith("-    \"downloadCount\":"));

			removedCount = removedCount_temp == null
				? 0
				: removedCount_temp[^1] == ','
					? int.Parse(removedCount_temp[22..^1])
					: int.Parse(removedCount_temp[22..]);
		}
		
		
		DownloadCountChange = DownloadCount - removedCount;
	}
}

public class Entry
{
	public string Repo { get; set; }
	public DownloadCountUpdate[] Updates { get; set; }
}

public class DownloadCountUpdate
{
	public long UnixTimestamp { get; set; }
	public int DownloadCount { get; set; }
}