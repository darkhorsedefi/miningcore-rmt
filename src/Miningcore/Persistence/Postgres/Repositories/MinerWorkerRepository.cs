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
    public async Task<Model.MinerWorkerStats> GetWorkerStatsAsync(
        IDbConnection con, IDbTransaction tx,
        string poolId, string miner, string worker)
    {
        const string selectQuery = @"
            SELECT *
              FROM workerstats
             WHERE poolid = @poolId
               AND miner  = @miner
               AND worker = @worker";

        // Query the Postgres entity
        var entity = await con.QuerySingleOrDefaultAsync<Entity.MinerWorkerStats>(
            selectQuery,
            new { poolId, miner, worker },
            tx);

        if (entity == null)
            return null;

        // tally share counts and blocks on the entity
        entity.ValidShares   = await con.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM shares WHERE poolid=@poolId AND miner=@miner AND worker=@worker",
            new { poolId, miner, worker }, tx);

        entity.InvalidShares = await con.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM shareerrors WHERE poolid=@poolId AND miner=@miner AND worker=@worker",
            new { poolId, miner, worker }, tx);

        entity.FoundBlocks   = await con.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM blocks WHERE poolid=@poolId AND miner=@miner AND worker=@worker AND status='confirmed'",
            new { poolId, miner, worker }, tx);

        // Map into the domain model
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
