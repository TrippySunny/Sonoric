namespace Sonoric.Models;

public sealed class UpdateInfo
{
    public required Version Version { get; init; }
    public required string Tag { get; init; }
    public required string Title { get; init; }
    public required string AssetName { get; init; }
    public required string DownloadUrl { get; init; }
    public long SizeBytes { get; init; }
    public string Rid { get; init; } = string.Empty;
}
