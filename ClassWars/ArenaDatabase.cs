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
    public class Database
    {
        private IDbConnection _db;

        public Database(IDbConnection db)
        {
            _db = db;
            var sqlCreator = new SqlTableCreator(_db,
                _db.GetSqlType() == SqlType.Sqlite
                    ? (IQueryBuilder)new SqliteQueryCreator()
                    : new MysqlQueryCreator());
            var table = new SqlTable("CWArenas",
                new SqlColumn("ID", MySqlDbType.Int32) { AutoIncrement = true, Primary = true },
                new SqlColumn("Name", MySqlDbType.String),
                new SqlColumn("HostX", MySqlDbType.Int32),
                new SqlColumn("HostY", MySqlDbType.Int32),
                new SqlColumn("RedSpawnX", MySqlDbType.Int32),
                new SqlColumn("RedSpawnY", MySqlDbType.Int32),
                new SqlColumn("BlueSpawnX", MySqlDbType.Int32),
                new SqlColumn("BlueSpawnY", MySqlDbType.Int32),
                new SqlColumn("ArenaTopLeftX", MySqlDbType.Int32),
                new SqlColumn("ArenaTopLeftY", MySqlDbType.Int32),
                new SqlColumn("ArenaBottomRightX", MySqlDbType.Int32),
                new SqlColumn("ArenaBottomRightY", MySqlDbType.Int32),
                new SqlColumn("SwitchPosX", MySqlDbType.Int32),
                new SqlColumn("SwitchPosY", MySqlDbType.Int32));
            sqlCreator.EnsureTableStructure(table);
        }

        public static Database InitDb(string name)
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
            var database = new Database(db);
            return database;
        }

        public QueryResult QueryReader(string query, params object[] args)
        {
            return _db.QueryReader(query, args);
        }

        public int Query(string query, params object[] args)
        {
            return _db.Query(query, args);
        }

        public int AddArena(Arena copy)
        {
            Query("INSERT INTO CWArenas (Name, HostX, HostY, RedSpawnX, RedSpawnY, BlueSpawnX, BlueSpawnY, ArenaTopLeftX, ArenaTopLeftY, ArenaBottomRightX, ArenaBottomRightY, SwitchPosX, SwitchPosY) VALUES (@0, @1, @2, @3, @4, @5, @6, @7, @8, @9, @10, @11, @12)",
                copy.name, copy.host.X, copy.host.Y, copy.rSpawn.X, copy.rSpawn.Y, copy.bSpawn.X, copy.bSpawn.Y, copy.arenaTopL.X, copy.arenaTopL.Y, copy.arenaBottomR.X, copy.arenaBottomR.Y, copy.switchPos.X, copy.switchPos.Y);

            using (var reader = QueryReader("SELECT max(ID) FROM CWArenas"))
            {
                if (reader.Read())
                {
                    var id = reader.Get<int>("max(ID)");
                    return id;
                }
            }

            return -1;
        }

        public void DeleteArena(int id)
        {
            Query("DELETE FROM CWArenas WHERE ID = @0", id);
        }

        public void DeleteArenaByName(string name)
        {
            Query("DELETE FROM CWArenas WHERE Name = @0", name);
        }

        public void UpdateArena(Arena update)
        {
            var query =
                string.Format(
                    "UPDATE CWArenas SET Name = {0}, HostX = {1}, HostY = {2}, RedSpawnX = {3}, RedSpawnY = {4}, BlueSpawnX = {5}, BlueSpawnY = {6}, ArenaTopLeftX = {7}, ArenaTopLeftY = {8}, ArenaBottomRightX = {9}, ArenaBottomRightY = {10}, SwitchPosX = {11}, SwitchPosY = {12} WHERE ID = @0 ",
                    update.name, update.host.X, update.host.Y, update.rSpawn.X, update.rSpawn.Y, update.bSpawn.X, update.bSpawn.Y, update.arenaTopL.X, update.arenaTopL.Y, update.arenaBottomR.X, update.arenaBottomR.Y, update.switchPos.X, update.switchPos.Y);

            Query(query, update.id);
        }

        public void LoadArenas(ref List<Arena> list)
        {
            using (var reader = QueryReader("SELECT * FROM CWArenas"))
            {
                while (reader.Read())
                {
                    var id = reader.Get<int>("ID");
                    var Name = reader.Get<string>("Name");
                    Vector2 host = new Vector2(reader.Get<float>("HostX"), reader.Get<float>("HostY"));
                    Vector2 RedSpawn = new Vector2(reader.Get<float>("RedSpawnX"), reader.Get<float>("RedSpawnY"));
                    Vector2 BlueSpawn = new Vector2(reader.Get<float>("BlueSpawnX"), reader.Get<float>("BlueSpawnY"));
                    Vector2 ArenaTopL = new Vector2(reader.Get<float>("ArenaTopLeftX"), reader.Get<float>("ArenaTopLeftY"));
                    Vector2 ArenaBottomRight = new Vector2(reader.Get<float>("ArenaBottomRightX"), reader.Get<float>("ArenaBottomRightY"));
                    Vector2 switchPos = new Vector2(reader.Get<float>("SwitchPosX"), reader.Get<float>("SwitchPosY"));

                    var arena = new Arena(Name, host, RedSpawn, BlueSpawn, ArenaTopL, ArenaBottomRight, switchPos) { id = id };
                    list.Add(arena);
                }
            }
        }
    }
}