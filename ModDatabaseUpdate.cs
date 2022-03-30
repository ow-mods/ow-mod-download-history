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
