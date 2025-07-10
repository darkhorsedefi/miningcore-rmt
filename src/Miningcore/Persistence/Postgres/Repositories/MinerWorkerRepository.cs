using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Dapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Entity = Miningcore.Persistence.Postgres.Entities.MinerWorkerStats;

namespace Miningcore.Persistence.Postgres.Repositories
{
    public class MinerWorkerRepository : IMinerWorkerRepository
    {
        private readonly IMapper mapper;

        public MinerWorkerRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        /// <summary>
        /// Returns per-worker stats (including uptime/session tracking).
        /// </summary>
        public async Task<MinerWorkerStats> GetWorkerStatsAsync(
            IDbConnection con, IDbTransaction tx,
            string poolId, string miner, string worker)
        {
            var now = DateTime.UtcNow;

            // 1) load persisted row (if any)
            var statsEntity = await con.QuerySingleOrDefaultAsync<Entity>(
                @"SELECT * 
                    FROM workerstats
                   WHERE poolid = @poolId
                     AND miner  = @miner
                     AND worker = @worker",
                new { poolId, miner, worker }, tx);

            // 2) tally shares & confirmed blocks
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

            // 3) best share difficulty: persisted if present, otherwise from shares
            var bestDifficulty = statsEntity != null
                ? statsEntity.BestDifficulty
                : await con.ExecuteScalarAsync<double>(
                    @"SELECT COALESCE(MAX(difficulty), 0)
                        FROM shares
                       WHERE poolid = @poolId
                         AND miner  = @miner
                         AND worker = @worker",
                    new { poolId, miner, worker }, tx);

            // 4) first seen time: persisted Created if present, else earliest share or now
            var firstSeen = statsEntity != null
                ? statsEntity.Created
                : (await con.ExecuteScalarAsync<DateTime?>(
                    @"SELECT MIN(created)
                        FROM shares
                       WHERE poolid = @poolId
                         AND miner  = @miner
                         AND worker = @worker",
                    new { poolId, miner, worker }, tx) 
                   ?? now);

            // 5) read session start (for uptime)
            var sessionStart = statsEntity?.SessionStart;

            // 6) build domain DTO
            return new MinerWorkerStats
            {
                PoolId         = poolId,
                Miner          = miner,
                Worker         = worker,
                BestDifficulty = bestDifficulty,
                Difficulty     = bestDifficulty,   // show best‐share here
                Created        = firstSeen,
                Updated        = now,
                ValidShares    = validShares,
                InvalidShares  = invalidShares,
                FoundBlocks    = foundBlocks,
                SessionStart   = sessionStart
            };
        }

        /// <summary>
        /// Marks the start of a new worker session (on connect/authorize).
        /// </summary>
        public Task StartSessionAsync(
            IDbConnection con, IDbTransaction tx,
            string poolId, string miner, string worker)
        {
            const string sql = @"
                INSERT INTO workerstats(poolid, miner, worker, sessionstart, created, updated)
                VALUES(@poolId, @miner, @worker, now(), now(), now())
                ON CONFLICT (poolid, miner, worker) DO
                  UPDATE SET sessionstart = now(), updated = now();";

            return con.ExecuteAsync(sql, new { poolId, miner, worker }, tx);
        }

        /// <summary>
        /// Clears session start (on disconnect).
        /// </summary>
        public Task EndSessionAsync(
            IDbConnection con, IDbTransaction tx,
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

        /// <summary>
        /// Returns all workers for a miner.
        /// </summary>
        public async Task<MinerWorkerStats[]> GetWorkerStatsAsync(
            IDbConnection con, IDbTransaction tx,
            string poolId, string miner)
        {
            const string query = @"SELECT worker FROM workerstats WHERE poolid = @poolId AND miner = @miner";

            var workers = await con.QueryAsync<string>(query, new { poolId, miner }, tx);

            var list = new List<MinerWorkerStats>(workers.Count());
            foreach (var w in workers)
            {
                var stats = await GetWorkerStatsAsync(con, tx, poolId, miner, w);
                if (stats != null)
                    list.Add(stats);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Returns all workers across all miners in a pool.
        /// </summary>
        public async Task<MinerWorkerStats[]> GetWorkerStatsAsync(
            IDbConnection con, IDbTransaction tx,
            string poolId)
        {
            const string query = @"SELECT miner, worker FROM workerstats WHERE poolid = @poolId";

            var rows = await con.QueryAsync<(string Miner, string Worker)>(query, new { poolId }, tx);

            var list = new List<MinerWorkerStats>(rows.Count());
            foreach (var (m, w) in rows)
            {
                var stats = await GetWorkerStatsAsync(con, tx, poolId, m, w);
                if (stats != null)
                    list.Add(stats);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Persists best‐share difficulty/difficulty fields (legacy).
        /// </summary>
        public Task UpdateWorkerStatsAsync(
            IDbConnection con, IDbTransaction tx,
            MinerWorkerStats settings)
        {
            const string sql = @"
                INSERT INTO workerstats(poolid, miner, worker, bestdifficulty, difficulty, created, updated)
                VALUES(@PoolId, @Miner, @Worker, @BestDifficulty, @Difficulty, @Created, @Updated)
                ON CONFLICT (poolid, miner, worker) DO UPDATE
                  SET bestdifficulty = EXCLUDED.bestdifficulty,
                      difficulty     = EXCLUDED.difficulty,
                      updated        = EXCLUDED.updated;";

            return con.ExecuteAsync(sql, settings, tx);
        }
    }
}
