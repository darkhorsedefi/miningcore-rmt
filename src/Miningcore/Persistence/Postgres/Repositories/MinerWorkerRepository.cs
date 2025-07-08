using System.Data;
using AutoMapper;
using Dapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Persistence.Postgres.Entities;

namespace Miningcore.Persistence.Postgres.Repositories;

public class MinerWorkerRepository : IMinerWorkerRepository
{
    public MinerWorkerRepository(IMapper mapper)
    {
        this.mapper = mapper;
    }

    private readonly IMapper mapper;

    public async Task<MinerWorkerStats> GetWorkerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, string worker)
    {
        const string selectQuery = @"
            SELECT *
                FROM workerstats
                WHERE poolid = @poolId
                AND miner  = @miner
                AND worker = @worker";

        var statsEntity = await con.QuerySingleOrDefaultAsync<Entities.MinerWorkerStats>(
            selectQuery,
            new { poolId, miner, worker },
            tx);

        if (statsEntity == null)
            return null;

        // Count valid shares
        statsEntity.ValidShares = await con.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*)
                FROM shares
                WHERE poolid = @poolId
                    AND miner  = @miner
                    AND worker = @worker",
            new { poolId, miner, worker },
            tx);

        // Count invalid shares
        statsEntity.InvalidShares = await con.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*)
                FROM shareerrors
                WHERE poolid = @poolId
                    AND miner  = @miner
                    AND worker = @worker",
            new { poolId, miner, worker },
            tx);

        // Count found blocks
        statsEntity.FoundBlocks = await con.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*)
                FROM blocks
                WHERE poolid = @poolId
                    AND miner  = @miner
                    AND worker = @worker
                    AND status = 'confirmed'",
            new { poolId, miner, worker },
            tx);

        // Map to domain model
        var result = new MinerWorkerStats
        {
            PoolId         = statsEntity.PoolId,
            Miner          = statsEntity.Miner,
            Worker         = statsEntity.Worker,
            Created        = statsEntity.Created,
            Updated        = statsEntity.Updated,
            BestDifficulty = statsEntity.BestDifficulty,
            Difficulty     = statsEntity.Difficulty,
            ValidShares    = statsEntity.ValidShares,
            InvalidShares  = statsEntity.InvalidShares,
            FoundBlocks    = statsEntity.FoundBlocks
        };

        return result;
    }

    public async Task<MinerWorkerStats[]> GetWorkerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner)
    {
        const string query = @"SELECT * FROM workerstats WHERE poolid = @poolId AND miner = @miner";

        return (await con.QueryAsync<Entities.MinerWorkerStats>(new CommandDefinition(query, new { poolId, miner })))
            .Select(mapper.Map<MinerWorkerStats>)
            .ToArray();
    }

    public async Task<MinerWorkerStats[]> GetWorkerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId)
    {
        const string query = @"SELECT * FROM workerstats WHERE poolid = @poolId";

        return (await con.QueryAsync<Entities.MinerWorkerStats>(new CommandDefinition(query, new { poolId })))
            .Select(mapper.Map<MinerWorkerStats>)
            .ToArray();
    }

    public Task UpdateWorkerStatsAsync(IDbConnection con, IDbTransaction tx, MinerWorkerStats settings)
    {
        const string query = @"INSERT INTO workerstats(poolid, miner, worker, bestdifficulty, created, updated)
            VALUES(@poolid, @miner, @worker, @bestdifficulty, now(), now())
            ON CONFLICT ON CONSTRAINT workerstats_pkey DO UPDATE
            SET bestdifficulty = @bestdifficulty, updated = now()
            WHERE workerstats.poolid = @poolid AND workerstats.miner = @miner AND workerstats.worker = @worker";

        return con.ExecuteAsync(query, settings, tx);
    }
}
