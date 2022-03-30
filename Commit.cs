using System.Globalization;

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