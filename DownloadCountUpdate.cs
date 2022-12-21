public class DownloadCountUpdate
{
	public long UnixTimestamp { get; set; }
	public int DownloadCount { get; set; }

	public DownloadCountUpdate(long unixTimestamp, int downloadCount)
	{
		UnixTimestamp = unixTimestamp;
		DownloadCount = downloadCount;
	}
}