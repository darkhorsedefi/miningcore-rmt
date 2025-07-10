using System.Data;
using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Repositories;

public interface IMinerWorkerRepository
{
    Task<MinerWorkerStats> GetWorkerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId, string address, string worker);
    Task<MinerWorkerStats[]> GetWorkerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId, string address);
    Task<MinerWorkerStats[]> GetWorkerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId);
    Task StartSessionAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, string worker);
    Task EndSessionAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, string worker);
    Task UpdateWorkerStatsAsync(IDbConnection con, IDbTransaction tx, MinerWorkerStats settings);
}
