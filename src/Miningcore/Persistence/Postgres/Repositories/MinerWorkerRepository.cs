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
        // Now = “as of” for uptime
        var now = DateTime.UtcNow;

        // 1) Try to load any existing workerstats row
        var entity = await con.QuerySingleOrDefaultAsync<Entity.MinerWorkerStats>(
            @"SELECT * 
                FROM workerstats
            WHERE poolid = @poolId
                AND miner  = @miner
                AND worker = @worker",
            new { poolId, miner, worker }, tx);

        // 2) Always tally up shares & blocks
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

        // 3) Compute best-share difficulty from the shares table if no row exists
        var bestShareDiff = entity != null
            ? entity.BestDifficulty
            : await con.ExecuteScalarAsync<double>(
                @"SELECT COALESCE(MAX(difficulty), 0) 
                    FROM shares
                WHERE poolid = @poolId
                    AND miner  = @miner
                    AND worker = @worker",
                new { poolId, miner, worker }, tx);

        // 4) Compute the “first seen” timestamp if no row exists
        var firstShareTime = entity != null
            ? entity.Created
            : (await con.ExecuteScalarAsync<DateTime?>(
                @"SELECT MIN(created) 
                    FROM shares
                    WHERE poolid = @poolId
                    AND miner  = @miner
                    AND worker = @worker",
                new { poolId, miner, worker }, tx)
            ?? now);

        // 5) Build and return the DTO, using now for Updated so uptime = now–Created
        return new Model.MinerWorkerStats
        {
            PoolId         = poolId,
            Miner          = miner,
            Worker         = worker,
            BestDifficulty = bestShareDiff,
            Difficulty     = bestShareDiff,       // show best‐share diff in your “Difficulty” column
            Created        = firstShareTime,
            Updated        = now,
            ValidShares    = validShares,
            InvalidShares  = invalidShares,
            FoundBlocks    = foundBlocks
        };
    }

    // Call this when the worker (re)connects
    public Task StartSessionAsync(IDbConnection con, IDbTransaction tx,
        string poolId, string miner, string worker)
    {
        const string sql = @"
        INSERT INTO workerstats(poolid, miner, worker, sessionstart, created, updated)
        VALUES(@poolId,@miner,@worker, now(), now(), now())
        ON CONFLICT (poolid, miner, worker)
        DO UPDATE SET sessionstart = now(), updated = now();";

        return con.ExecuteAsync(sql, new { poolId, miner, worker }, tx);
    }

    // Call this when the worker disconnects
    public Task EndSessionAsync(IDbConnection con, IDbTransaction tx,
        string poolId, string miner, string worker)
    {
        const string sql = @"
        UPDATE workerstats
            SET sessionstart = NULL,
                updated      = now()
        WHERE poolid = @poolId
            AND miner  = @miner
            AND worker = @worker;";

        return con.ExecuteAsync(sql, new { poolId, miner, worker }, tx);
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
