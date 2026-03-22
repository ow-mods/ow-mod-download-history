public class DownloadCountUpdate
{
	public long T { get; set; }
	public int D { get; set; }

	public DownloadCountUpdate(long unixTimestamp, int downloadCount)
	{
		T = unixTimestamp;
		D = downloadCount;
	}
}