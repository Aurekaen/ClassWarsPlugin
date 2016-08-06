using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using TShockAPI;
using TShockAPI.DB;

namespace ClassWars
{
    public class StatDatabase
    {
        private IDbConnection _db;

        public StatDatabase(IDbConnection db)
        {
            _db = db;
            var sqlCreator = new SqlTableCreator(_db,
                _db.GetSqlType() == SqlType.Sqlite
                    ? (IQueryBuilder)new SqliteQueryCreator()
                    : new MysqlQueryCreator());
            var table = new SqlTable("PlayerStats",
                new SqlColumn("Account", MySqlDbType.String),
                new SqlColumn("Kills", MySqlDbType.Float),
                new SqlColumn("Deaths", MySqlDbType.Float),
                new SqlColumn("Wins", MySqlDbType.Float),
                new SqlColumn("Losses", MySqlDbType.Float),
                new SqlColumn("GamesCompleted", MySqlDbType.Float),
                new SqlColumn("GamesLeft", MySqlDbType.Float),
                new SqlColumn("GamesFilled", MySqlDbType.Float),
                new SqlColumn("TimePlayed", MySqlDbType.Float),
                new SqlColumn("TimeIncomplete", MySqlDbType.Float));
            sqlCreator.EnsureTableStructure(table);
        }

        public static StatDatabase InitDb(string name)
        {
            IDbConnection db;
            if (TShock.Config.StorageType.ToLower() == "sqlite")
                db =
                    new SqliteConnection(string.Format("uri=file://{0},Version=3",
                        Path.Combine(TShock.SavePath, name + ".sqlite")));
            else if (TShock.Config.StorageType.ToLower() == "mysql")
            {
                try
                {
                    var host = TShock.Config.MySqlHost.Split(':');
                    db = new MySqlConnection
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4}",
                            host[0],
                            host.Length == 1 ? "3306" : host[1],
                            TShock.Config.MySqlDbName,
                            TShock.Config.MySqlUsername,
                            TShock.Config.MySqlPassword
                            )
                    };
                }
                catch (MySqlException x)
                {
                    TShock.Log.Error(x.ToString());
                    throw new Exception("MySQL not setup correctly.");
                }
            }
            else
                throw new Exception("Invalid storage type.");
            var statDatabase = new StatDatabase(db);
            return statDatabase;
        }

        public QueryResult QueryReader(string query, params object[] args)
        {
            return _db.QueryReader(query, args);
        }

        public int Query(string query, params object[] args)
        {
            return _db.Query(query, args);
        }

        public int AddPlayerStat(PlayerStat copy)
        {
            Query("INSERT INTO PlayerStats (Account, Kills, Deaths, Wins, Losses, GamesCompleted, GamesLeft, GamesFilled, TimePlayed, TimeIncomplete) VALUES (@0, @1, @2, @3, @4, @5, @6, @7, @8, @9)",
                copy.account, copy.kills, copy.deaths, copy.wins, copy.losses, copy.gamesCompleted, copy.gamesLeft, copy.gamesFilled, copy.timePlayed, copy.timeIncomplete);

            using (var reader = QueryReader("SELECT max(ID) FROM PlayerStats"))
            {
                if (reader.Read())
                {
                    var id = reader.Get<int>("max(ID)");
                    return id;
                }
            }

            return -1;
        }

        public void DeletePlayerStat(string account)
        {
            Query("DELETE FROM PlayerStats WHERE Account = @0", account);
        }

        public void UpdatePlayerStat(PlayerStat update)
        {
            var query =
                string.Format(
                    "UPDATE PlayerStats SET Kills = {0}, Deaths = {1}, Wins = {2}, Losses = {3}, GamesCompleted = {4}, GamesLeft = {5}, GamesFilled = {6}, TimePlayed = {7}, TimeIncomplete = {8}  WHERE Account = @0",
                    update.kills, update.deaths, update.wins, update.losses, update.gamesCompleted, update.gamesLeft, update.gamesFilled, update.timePlayed, update.timePlayed);

            Query(query, update.account);
        }

        public void LoadPlayerStats(ref List<PlayerStat> list)
        {
            using (var reader = QueryReader("SELECT * FROM PlayerStats"))
            {
                while (reader.Read())
                {
                    var account = reader.Get<string>("Account");
                    var kills = reader.Get<float>("Kills");
                    var deaths = reader.Get<float>("Deaths");
                    var wins = reader.Get<float>("Wins");
                    var losses = reader.Get<float>("Losses");
                    var gamesCompleted = reader.Get<float>("GamesCompleted");
                    var gamesLeft = reader.Get<float>("GamesLeft");
                    var gamesFilled = reader.Get<float>("GamesFilled");
                    var timePlayed = reader.Get<float>("TimePlayed");
                    var timeIncomplete = reader.Get<float>("TimeIncomplete");

                    var playerStat = new PlayerStat(account, kills, deaths, wins, losses, gamesCompleted, gamesLeft, gamesFilled, timePlayed, timeIncomplete);
                    list.Add(playerStat);
                }
            }
        }
    }
}