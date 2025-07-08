namespace Miningcore.Api.Responses;

public class WorkerStats
{
    public string Miner { get; set; }
    public string Worker { get; set; }
    public double BestDifficulty { get; set; }
    public long ValidShares { get; set; }
    public long InvalidShares { get; set; }
    public long FoundBlocks { get; set; }
    public TimeSpan Uptime { get; set; }
    public double Difficulty { get; set; }
}

public class WorkerStatsResponse
{
    public WorkerStats[] WorkerStats { get; set; }
}
