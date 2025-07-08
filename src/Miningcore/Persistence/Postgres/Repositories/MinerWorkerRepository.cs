using System.Data;
using AutoMapper;
using Dapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Persistence.Postgres.Entities;

using Model   = Miningcore.Persistence.Model;
using Entity  = Miningcore.Persistence.Postgres.Entities;

namespace Miningcore.Persistence.Postgres.Repositories;

public class MinerWorkerRepository : IMinerWorkerRepository
{
    public MinerWorkerRepository(IMapper mapper)
    {
        this.mapper = mapper;
    }

    private readonly IMapper mapper;

    public async Task<Model.MinerWorkerStats> GetWorkerStatsAsync(
    IDbConnection con, IDbTransaction tx,
    string poolId, string miner, string worker)
    {
        // 1) Attempt to read an existing row
        var entity = await con.QuerySingleOrDefaultAsync<Entity.MinerWorkerStats>(
            @"SELECT * 
                FROM workerstats
            WHERE poolid = @poolId
                AND miner  = @miner
                AND worker = @worker",
            new { poolId, miner, worker }, tx);

        // 2) Always count shares & blocks
        var validShares = await con.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*) FROM shares
                WHERE poolid = @poolId
                AND miner  = @miner
                AND worker = @worker",
            new { poolId, miner, worker }, tx);

        var invalidShares = await con.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*) FROM shareerrors
                WHERE poolid = @poolId
                AND miner  = @miner
                AND worker = @worker",
            new { poolId, miner, worker }, tx);

        var foundBlocks = await con.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*) FROM blocks
                WHERE poolid = @poolId
                AND miner  = @miner
                AND worker = @worker
                AND status = 'confirmed'",
            new { poolId, miner, worker }, tx);

        // 3) No row → return a new DTO with just the counts
        if (entity == null)
        {
            return new Model.MinerWorkerStats
            {
                PoolId         = poolId,
                Miner          = miner,
                Worker         = worker,
                BestDifficulty = 0,
                Difficulty     = 0,
                Created        = DateTime.MinValue,
                Updated        = DateTime.MinValue,
                ValidShares    = validShares,
                InvalidShares  = invalidShares,
                FoundBlocks    = foundBlocks
            };
        }

        // 4) Row exists → override the counters & return
        entity.ValidShares   = validShares;
        entity.InvalidShares = invalidShares;
        entity.FoundBlocks   = foundBlocks;

        return new Model.MinerWorkerStats
        {
            PoolId         = entity.PoolId,
            Miner          = entity.Miner,
            Worker         = entity.Worker,
            BestDifficulty = entity.BestDifficulty,
            Difficulty     = entity.Difficulty,
            Created        = entity.Created,
            Updated        = entity.Updated,
            ValidShares    = entity.ValidShares,
            InvalidShares  = entity.InvalidShares,
            FoundBlocks    = entity.FoundBlocks
        };
    }


    public async Task<Model.MinerWorkerStats[]> GetWorkerStatsAsync(
        IDbConnection con, IDbTransaction tx,
        string poolId, string miner)
    {
        const string query = @"SELECT * FROM workerstats WHERE poolid=@poolId AND miner=@miner";

        var entities = await con.QueryAsync<Entity.MinerWorkerStats>(
            query, new { poolId, miner }, tx);

        var list = new List<Model.MinerWorkerStats>();
        foreach (var e in entities)
        {
            var stats = await GetWorkerStatsAsync(con, tx, poolId, miner, e.Worker);
            if (stats != null)
                list.Add(stats);
        }

        return list.ToArray();
    }

    public async Task<Model.MinerWorkerStats[]> GetWorkerStatsAsync(
        IDbConnection con, IDbTransaction tx,
        string poolId)
    {
        const string query = @"SELECT * FROM workerstats WHERE poolid=@poolId";

        var entities = await con.QueryAsync<Entity.MinerWorkerStats>(
            query, new { poolId }, tx);

        var list = new List<Model.MinerWorkerStats>();
        foreach (var e in entities)
        {
            var stats = await GetWorkerStatsAsync(con, tx, poolId, e.Miner, e.Worker);
            if (stats != null)
                list.Add(stats);
        }

        return list.ToArray();
    }

    public Task UpdateWorkerStatsAsync(
        IDbConnection con, IDbTransaction tx,
        Model.MinerWorkerStats settings)
    {
        const string query = @"
            INSERT INTO workerstats(poolid, miner, worker, bestdifficulty, difficulty, created, updated)
            VALUES(@PoolId, @Miner, @Worker, @BestDifficulty, @Difficulty, @Created, @Updated)
            ON CONFLICT (poolid, miner, worker)
            DO UPDATE SET
                bestdifficulty = EXCLUDED.bestdifficulty,
                difficulty     = EXCLUDED.difficulty,
                updated        = EXCLUDED.updated";

        return con.ExecuteAsync(query, settings, tx);
    }
}
