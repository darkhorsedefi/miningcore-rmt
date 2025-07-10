namespace Miningcore.Persistence.Model;

public class MinerWorkerStats
{
    public string PoolId { get; set; }
    public string Miner { get; set; }
    public string Worker { get; set; }
    public double BestDifficulty { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public long ValidShares { get; set; }
    public long InvalidShares { get; set; }
    public long FoundBlocks { get; set; }
    public double Difficulty { get; set; }
    public DateTime? SessionStart { get; set; }
    public TimeSpan Uptime { get; set; }
}
