using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using System.IO;
using System.IO.Streams;
using System.Threading;

namespace ClassWars
{
    [ApiVersion(1, 23)]
    public class ClassWars : TerrariaPlugin
    {
        public override string Name { get { return "ClassWars"; } }
        public override string Author { get { return "Alec"; } }
        public override string Description { get { return "Automatic Class Wars hosting plugin."; } }
        public override Version Version { get { return new Version(1, 0, 0, 0); } }

        #region variables
        public static Dictionary<string, int> Colors = new Dictionary<string, int>();
        private static Database arena_db;
        //private static StatDatabase stat_db;
        //private static List<PlayerStat> _playerStats = new List<PlayerStat>();
        private static List<Arena> _arenas = new List<Arena>();
        private static List<string> arenaNames = new List<string>();
        private string GameInProgress = "none";
        private List<TSPlayer> redTeam = new List<TSPlayer>() { null };
        private List<TSPlayer> blueTeam = new List<TSPlayer>() { null };
        public int redPaintID, bluePaintID, blueBunkerCount, redBunkerCount, arenaIndex;
        //private TSPlayer killer;
        #endregion
        public ClassWars(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            arena_db = Database.InitDb("CWArenas");
            //stat_db = StatDatabase.InitDb("PlayerStats");
            arena_db.LoadArenas(ref _arenas);
            //stat_db.LoadPlayerStats(ref _playerStats);
            arenaNameReload();
            GameInProgress = "none";
            ServerApi.Hooks.NetGetData.Register(this, onGetData);
            ServerApi.Hooks.ServerLeave.Register(this, OnPlayerLeave);
            ServerApi.Hooks.GameUpdate.Register(this, onUpdate);
            Commands.ChatCommands.Add(new Command("cw.main", cw, "cw", "classwars"));
            //Commands.ChatCommands.Add(new Command("cw.stats", cwstats, "cwlog", "classwarslog", "cwstats", "classwarsstats"));

            Colors.Add("blank", 0);

            Main.player[Main.myPlayer] = new Player();
            var item = new Item();
            for (int i = -48; i < Main.maxItemTypes; i++)
            {
                item.netDefaults(i);
                if (item.paint > 0)
                    Colors.Add(item.name.Substring(0, item.name.Length - 6).ToLowerInvariant(), item.paint);
            }
            bluePaintID = GetColorID("blue")[0];
            redPaintID = GetColorID("red")[0];
        }
        public static List<int> GetColorID(string color)
        {
            int ID;
            if (int.TryParse(color, out ID) && ID >= 0 && ID < Main.numTileColors)
                return new List<int> { ID };

            var list = new List<int>();
            foreach (var kvp in Colors)
            {
                if (kvp.Key == color)
                    return new List<int> { kvp.Value };
                if (kvp.Key.StartsWith(color))
                    list.Add(kvp.Value);
            }
            return list;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NetGetData.Deregister(this, onGetData);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnPlayerLeave);
                ServerApi.Hooks.GameUpdate.Deregister(this, onUpdate);
            }
            base.Dispose(disposing);
        }

        private void onGetData(GetDataEventArgs args)
        {
            if (GameInProgress == "none")
                return;
            if (args.MsgID == PacketTypes.Tile)
                using (var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
                {
                    int action = data.ReadByte();
                    if (action == 0)
                    {
                        CheckWins();
                    }
                }
            if (args.MsgID == PacketTypes.PlayerSpawn)
            {
                if (redTeam.Contains(TShock.Players[args.Msg.whoAmI]))
                {
                    TShock.Players[args.Msg.whoAmI].Teleport(_arenas[arenaIndex].rSpawn.X * 16, _arenas[arenaIndex].rSpawn.Y * 16);
                    return;
                }
                if (blueTeam.Contains(TShock.Players[args.Msg.whoAmI]))
                {
                    TShock.Players[args.Msg.whoAmI].Teleport(_arenas[arenaIndex].bSpawn.X * 16, _arenas[arenaIndex].bSpawn.Y * 16);
                    return;
                }
            }

        }

        private void OnPlayerLeave(LeaveEventArgs args)
        {
            if (redTeam.Contains(TShock.Players[args.Who]))
            {
                redTeam.Remove(TShock.Players[args.Who]);
            }
            if (blueTeam.Contains(TShock.Players[args.Who]))
            {
                blueTeam.Remove(TShock.Players[args.Who]);
            }
        }

        /*private static bool HandlePlayerKillMe(GetDataHandlerArgs args)
        {
            int index = args.Player.Index; //Attacking Player
            byte PlayerID = (byte)args.Data.ReadByte();
            byte hitDirection = (byte)args.Data.ReadByte();
            Int16 Damage = (Int16)args.Data.ReadInt16();
            bool PVP = args.Data.ReadBoolean();
            var player = TShock.Players[index];
            return false;
        }*/

        /*private void onGetData(GetDataEventArgs args)
        {
            PacketTypes type = args.MsgID;
            TSPlayer player = TShock.Players[args.Msg.whoAmI];
            if (player == null)
                return;
            if (!player.ConnectionAlive)
                return;
            if ((!redTeam.Contains(player)) && (!blueTeam.Contains(player)))
                return;
            if (type == PacketTypes.PlayerKillMe)
            {
                using (var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
                {
                    int index = player.Index; //Attacking Player
                    byte PlayerID = (byte)data.ReadByte();
                    byte hitDirection = (byte)data.ReadByte();
                    Int16 Damage = (Int16)data.ReadInt16();
                    bool PVP = data.ReadBoolean();
                    int plrLoc = _playerStats.FindIndex(delegate (PlayerStat plr) { return plr.account.ToLower() == TShock.Players[index].User.Name.ToLower(); });
                    int klrLoc = _playerStats.FindIndex(delegate (PlayerStat plr) { return plr.account.ToLower() == TShock.Players[PlayerID].User.Name.ToLower(); });
                    if (index != PlayerID)
                    {
                        _playerStats[plrLoc].deaths = _playerStats[plrLoc].deaths + 1;
                        _playerStats[klrLoc].kills = _playerStats[plrLoc].kills + 1;
                    }

                }
            }
            else if (type == PacketTypes.PlayerDamage)
            {
                using (var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
                {
                    int index = player.Index; //Attacking Player
                    byte PlayerID = (byte)data.ReadByte();
                    byte hitDirection = (byte)data.ReadByte();
                    Int16 damage = data.ReadInt16();
                    var attackingPlayer = TShock.Players[index];
                    bool PVP = data.ReadBoolean();
                    byte crit = (byte)data.ReadByte();
                    if ((redTeam.Contains(player)) || (blueTeam.Contains(player)))
                        killer = attackingPlayer;
                }
            }
        }*/

        private void onUpdate(EventArgs args)
        {
            if (GameInProgress != "none")
            {
                foreach (TSPlayer plr in redTeam)
                {
                    //Forces PVP for both teams for the duration of the game.
                    plr.TPlayer.hostile = true;
                    NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, "", plr.Index, 0f, 0f, 0f);
                }
                foreach (TSPlayer plr in blueTeam)
                {
                    plr.TPlayer.hostile = true;
                    NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, "", plr.Index, 0f, 0f, 0f);
                }
            }
        }

        private void arenaNameReload()
        {
            arenaNames.Clear();
            foreach (Arena arena in _arenas)
            {
                arenaNames.Add(arena.name.ToLower());
            }
        }

        /*private bool playerStatExists(string account)
        {
            if (_playerStats.Exists(delegate (PlayerStat player) { return player.account.ToLower() == account.ToLower(); }))
                return true;
            else
                return false;
        }

        private PlayerStat getStats(string account)
        {
            int index = _playerStats.FindIndex(delegate (PlayerStat player) { return player.account.ToLower() == account; });
            return _playerStats[index];
        }*/

        #region gameHandling
        private void gameStart()
        {
            if (GameInProgress != "none")
            {
                return;
            }
            Arena arena = _arenas[arenaIndex];
            GameInProgress = arena.name;
            foreach (TSPlayer player in TShock.Players)
            {
                if (player.TPlayer.team == 1)
                {
                    redTeam.Add(player);
                }
                if (player.TPlayer.team == 3)
                {
                    blueTeam.Add(player);
                }
            }
            if (blueTeam.Count() != redTeam.Count())
            {
                TShock.Utils.Broadcast("Warning: Teams are not equal. " + blueTeam.Count() + " Blue Players vs " + redTeam.Count() + "Red Players", Color.Red);
            }
            foreach (TSPlayer player in redTeam)
            {
                player.Teleport(arena.rSpawn.X * 16, arena.rSpawn.Y * 16);
            } 
            foreach (TSPlayer player in blueTeam)
            {
                player.Teleport(arena.bSpawn.X * 16, arena.bSpawn.Y * 16);
            }
            Wiring.HitSwitch((int) arena.switchPos.X * 16, (int) arena.switchPos.Y * 16);
            Thread threadHandler = new Thread(threadHandle);
            threadHandler.Start();
        }

        public void threadHandle()
        {
            Thread winThread = new Thread(ScoreUpdate);  
            for (;;)
            {
                if (GameInProgress == "none")
                    break;
                winThread.Start();
                Thread.Sleep(120000);
            }
        }

        public void ScoreUpdate()
        {
            Arena arena = _arenas[arenaIndex];
            blueBunkerCount = 0;
            redBunkerCount = 0;
            for (int i = 0; i <= arena.arenaBottomR.X - arena.arenaTopL.X; i++)
            {
                for (int j = 0; j <= arena.arenaBottomR.Y - arena.arenaTopL.Y; j++)
                {
                    int tile = Main.tile[(int)arena.arenaTopL.X + i, (int)arena.arenaTopL.Y + j].blockType();
                    int color;
                    if (tile == 25 || tile == 203 || tile == 117)
                    {
                        color = Main.tile[(int)arena.arenaTopL.X + i, (int)arena.arenaTopL.Y + j].wallColor();
                        if (color == bluePaintID)
                            blueBunkerCount += 1;
                        if (color == redPaintID)
                            redBunkerCount += 1;
                    }
                }
            }
            TShock.Utils.Broadcast("Red team's bunker has " + redBunkerCount.ToString() + " blocks remaining.", Color.Purple);
            TShock.Utils.Broadcast("Blue team's bunker has " + blueBunkerCount.ToString() + " blocks remaining.", Color.Purple);
        }

        public void CheckWins()
        {
            Arena arena = _arenas[arenaIndex];
            blueBunkerCount = 0;
            redBunkerCount = 0;
            for (int i = 0; i <= arena.arenaBottomR.X - arena.arenaTopL.X; i++)
            {
                for (int j = 0; j <= arena.arenaBottomR.Y - arena.arenaTopL.Y; j++)
                {
                    int tile = Main.tile[(int)arena.arenaTopL.X + i, (int)arena.arenaTopL.Y + j].type;
                    int color;
                    if (tile == 25 || tile == 203 || tile == 117)
                    {
                        color = Main.tile[(int)arena.arenaTopL.X + i, (int)arena.arenaTopL.Y + j].wallColor();
                        if (color == bluePaintID)
                            blueBunkerCount += 1;
                        if (color == redPaintID)
                            redBunkerCount += 1;
                    }
                }
            }
            if (blueBunkerCount == 0)
            {
                gameEnd(true);
                return;
            }
            if (redBunkerCount == 0)
            {
                gameEnd(false);
                return;
            }
        }

        private void gameEnd (bool winner)
        {
            GameInProgress = "none";
            redTeam.Clear();
            blueTeam.Clear();
            if (winner)
                TShock.Utils.Broadcast("Red Team Wins!", Color.HotPink);
            else
                TShock.Utils.Broadcast("Blue Team Win!", Color.HotPink);

            Arena arena = _arenas[arenaIndex];
            for (int i = 0; i <= arena.arenaBottomR.X - arena.arenaTopL.X; i++)
            {
                for (int j = 0; j <= arena.arenaBottomR.Y - arena.arenaTopL.Y; j++)
                {
                    int x = (int)arena.arenaTopL.X + i;
                    int y = (int)arena.arenaTopL.Y + j;
                    var tile = Main.tile[(int)arena.arenaTopL.X + i, (int)arena.arenaTopL.Y + j];
                    if (tile.wallColor() == bluePaintID || tile.wallColor() == redPaintID)
                    {
                        if (tile.wall == 195)
                            tile.type = 203;
                        if (tile.wall == 190)
                            tile.type = 25;
                        if (tile.wall == 200)
                            tile.type = 117;
                    }
                }
            }
            Wiring.HitSwitch((int)arena.switchPos.X, (int)arena.switchPos.Y);
        }
        #endregion

        #region CommandParsing
        private void cw(CommandArgs args)
        {
            var player = args.Player;
            #region help
            if ((args.Parameters.Count == 0) || (args.Parameters[0] == "help"))
            {
                player.SendErrorMessage("Aliases: /classwars, /cw");
                if (player.HasPermission("cw.start"))
                    player.SendErrorMessage("/cw start <arena>");
                if (player.HasPermission("cw.add"))
                {
                    player.SendErrorMessage("/cw add <arena>");
                    player.SendErrorMessage("/cw set <arena> <host|redspawn|bluespawn|arenabounds|switch>");
                }
                if (player.HasPermission("cw.del"))
                    player.SendErrorMessage("/cw del <arena>");
                if (player.HasPermission("tshock.admin.warp"))
                    player.SendErrorMessage("/cw goto <arena>");
                player.SendErrorMessage("/cw list");
                return;
            }
            #endregion
            var action = args.Parameters[0].ToLower();
            args.Parameters.RemoveAt(0);
            #region add
            if (action == "add")
            {
                if (!player.HasPermission("cw.add"))
                {
                    player.SendErrorMessage("You do not have permission to do this.");
                    return;
                }
                if (args.Parameters.Count == 0)
                {
                    player.SendErrorMessage("/cw add <arena>");
                    return;
                }
                string arenaName = args.Parameters[0].ToLower();
                if (arenaNames.Contains(arenaName))
                {
                    player.SendErrorMessage("There is already an arena by that name.");
                    return;
                }
                Vector2 temp = new Vector2(0, 0);
                Arena arena = new Arena(arenaName, temp, temp, temp, temp, temp, temp);
                arena_db.AddArena(arena);
                _arenas.Add(arena);
                arenaNameReload();
                return;
            }
            #endregion

            #region del
            if (action == "del")
            {
                if (!player.HasPermission("cw.del"))
                {
                    player.SendErrorMessage("You do not have permission do to this.");
                    return;
                }
                if (args.Parameters.Count == 0)
                {
                    player.SendErrorMessage("/cw del <arena>");
                    return;
                }
                var name = args.Parameters[0];
                if (GameInProgress != null)
                {
                    if (GameInProgress == name.ToLower())
                    {
                        player.SendErrorMessage("This arena is currently in use. You cannot delete it until the game is over.");
                        return;
                    }
                }
                if (!arenaNames.Contains(name.ToLower()))
                {
                    player.SendErrorMessage("No arenas by that name found.");
                    player.SendErrorMessage("Try /cw list");
                    return;
                }
                arenaNames.RemoveAll(delegate (string s) { return s == name; });
                arena_db.DeleteArenaByName(name);
                Arena temp = null;
                foreach (Arena arena in _arenas)
                {
                    if (arena.name == name)
                    {
                        temp = arena;
                    }
                }
                _arenas.Remove(temp);
                player.SendMessage("Arena " + temp.name + "removed.", Color.LimeGreen);
                return;
            }
            #endregion

            #region list
            if (action == "list")
            {
                int pagenum;
                if (!PaginationTools.TryParsePageNumber(args.Parameters, 0, args.Player, out pagenum))
                    return;
                IEnumerable<string> arenaList = from Arena in _arenas
                                                select Arena.name;
                PaginationTools.SendPage(player, pagenum, PaginationTools.BuildLinesFromTerms(arenaList),
                    new PaginationTools.Settings
                    {
                        HeaderFormat = "Class Wars arenas:",
                        FooterFormat = "type /cw list {{0}}",
                        NothingToDisplayString = "No Class Wars arenas are presently defined."
                    });
                return;
            }
            #endregion

            #region set
            if (action == "set")
            {
                if (!player.HasPermission("cw.add"))
                {
                    player.SendErrorMessage("You do not have permission to do this.");
                    return;
                }
                if (args.Parameters.Count < 2)
                {
                    player.SendErrorMessage("/cw set <arena> <host|redspawn|bluespawn|arenabounds|switch>");
                    return;
                }
                var arenaName = args.Parameters[0].ToLower();
                if (!arenaNames.Contains(arenaName))
                {
                    player.SendErrorMessage("Arena " + arenaName + "not found.");
                    return;
                }
                action = args.Parameters[1].ToLower();
                args.Parameters.RemoveAt(0);
                args.Parameters.RemoveAt(0);
                int index = _arenas.FindIndex(delegate (Arena arena) { return arena.name.ToLower() == arenaName; });

                #region host
                if (action == "host")
                {
                    _arenas[index].host = new Vector2(args.Player.TileX, args.Player.TileY);
                    arena_db.UpdateArena(_arenas[index]);
                    player.SendMessage(_arenas[index].name + "host location set to your position.", Color.LimeGreen);
                    return;
                }

                #endregion

                #region redspawn
                if (action == "redspawn")
                {
                    _arenas[index].rSpawn = new Vector2(args.Player.TileX, args.Player.TileY);
                    arena_db.UpdateArena(_arenas[index]);
                    player.SendMessage(_arenas[index].name + "red spawn location set to your position.", Color.LimeGreen);
                    return;
                }
                #endregion

                #region bluespawn
                if (action == "bluespawn")
                {
                    _arenas[index].bSpawn = new Vector2 (args.Player.TileX, args.Player.TileY);
                    arena_db.UpdateArena(_arenas[index]);
                    player.SendMessage(_arenas[index].name + "blue spawn location set to your position.", Color.LimeGreen);
                    return;
                }
                #endregion

                #region arenaBoundaries
                if ((action == "arenabounds") || (action == "arenaboundaries") || (action == "ab"))
                {
                    if (args.Parameters.Count == 0)
                    {
                        if (!player.TempPoints.Any(p => p == Point.Zero && args.Parameters[0].ToLower() == "define"))
                        {
                            Vector2 topLeft = new Vector2((Math.Min(player.TempPoints[0].X, player.TempPoints[1].X)), (Math.Min(player.TempPoints[0].Y, player.TempPoints[1].Y)));
                            Vector2 bottomRight = new Vector2((Math.Max(player.TempPoints[0].X, player.TempPoints[1].X)), (Math.Max(player.TempPoints[0].Y, player.TempPoints[1].Y)));
                            _arenas[index].arenaTopL = topLeft;
                            _arenas[index].arenaBottomR = bottomRight;
                            arena_db.UpdateArena(_arenas[index]);
                            player.SendMessage("Arena boundaries defined.", Color.LimeGreen);
                            return;
                        }
                        else
                        {
                            player.SendErrorMessage("Usage:");
                            player.SendErrorMessage("/cw set [arena] arenabounds <1/2>");
                            player.SendErrorMessage("/cw set [arena] arenabounds define");
                            return;
                        }
                    }
                    if (args.Parameters[0] == "1")
                    {
                        player.SendInfoMessage("Select point 1");
                        player.AwaitingTempPoint = 1;
                        return;
                    }
                    if (args.Parameters[0] == "2")
                    {
                        player.SendInfoMessage("Select point 2");
                        player.AwaitingTempPoint = 2;
                        return;
                    }
                    player.SendErrorMessage("Usage:");
                    player.SendErrorMessage("/cw set [arena] arenaboundaries <1/2>");
                    player.SendErrorMessage("");
                    player.SendErrorMessage("Once your points are set:");
                    player.SendErrorMessage("/cw set [arena] arenaboundaries");
                    return;
                }
                #endregion

                #region switch
                if (action == "switch")
                {
                    _arenas[index].bSpawn = new Vector2(args.Player.TileX, args.Player.TileY);
                    arena_db.UpdateArena(_arenas[index]);
                    player.SendMessage(_arenas[index].name + "Switch location set.", Color.LimeGreen);
                    return;
                }
                player.SendErrorMessage("Invalid Action");
                return;
                #endregion
            }
            #endregion

            #region goto
            if (action == "goto")
            {
                if (player.HasPermission("tshock.admin.warp"))
                {
                    if (args.Parameters.Count == 0)
                    {
                        player.SendErrorMessage("Usage: /cw goto <arena>");
                        return;
                    }
                    var arenaName = args.Parameters[0].ToLower();
                    if (!arenaNames.Contains(arenaName))
                    {
                        player.SendErrorMessage("Arena " + arenaName + "not found.");
                        return;
                    }
                    args.Parameters.RemoveAt(0);
                    int index = _arenas.FindIndex(delegate (Arena arena) { return arena.name.ToLower() == arenaName; });

                    if (player.Teleport(_arenas[index].host.X * 16, _arenas[index].host.Y * 16))
                        player.SendMessage("You have been teleported to " + _arenas[index].name + ".", Color.LimeGreen);
                    else
                        player.SendErrorMessage("Teleport failed!");
                    return;
                }
                else
                {
                    player.SendErrorMessage("You do not have permission to warp.");
                    return;
                }
            }
            #endregion

            #region start
            if (action == "start")
            {
                if (player.HasPermission("cw.start"))
                {
                    if (args.Parameters.Count == 0)
                    {
                        player.SendErrorMessage("Usage: /cw start <arena>");
                        return;
                    }
                    string arenaName = args.Parameters[0].ToLower();
                    if (!arenaNames.Contains(arenaName))
                    {
                        player.SendErrorMessage("Arena " + arenaName + "not found.");
                        return;
                    }
                    args.Parameters.RemoveAt(0);
                    int index = _arenas.FindIndex(delegate (Arena arena) { return arena.name.ToLower() == arenaName; });
                    arenaIndex = index;
                    if (GameInProgress == "none")
                        gameStart();
                    else
                        player.SendErrorMessage("Game already running!");
                    return;
                }
            }
            #endregion

            #region stop
            if (action == "stop")
            {
                if (player.HasPermission("cw.start"))
                {
                    GameInProgress = "none";
                    redTeam.Clear();
                    blueTeam.Clear();
                    TShock.Utils.Broadcast("Game Aborted Early", Color.Red);
                    Arena arena = _arenas[arenaIndex];
                    for (int i = 0; i <= arena.arenaBottomR.X - arena.arenaTopL.X; i++)
                    {
                        for (int j = 0; j <= arena.arenaBottomR.Y - arena.arenaTopL.Y; j++)
                        {
                            int x = (int)arena.arenaTopL.X + i;
                            int y = (int)arena.arenaTopL.Y + j;
                            var tile = Main.tile[(int)arena.arenaTopL.X + i, (int)arena.arenaTopL.Y + j];
                            if (tile.wallColor() == bluePaintID || tile.wallColor() == redPaintID)
                            {
                                if (tile.wall == 195)
                                    tile.type = 203;
                                if (tile.wall == 190)
                                    tile.type = 25;
                                if (tile.wall == 200)
                                    tile.type = 117;
                            }
                        }
                    }
                    Wiring.HitSwitch((int)arena.switchPos.X, (int)arena.switchPos.Y);
                    return;
                }
                else
                {
                    player.SendErrorMessage("You do not have permission to do this!");
                    return;
                }
            }
            #endregion

            #region join
            if (action == "join")
            {
                if (GameInProgress == "none")
                {
                    player.SendErrorMessage("No game running!");
                    return;
                }
                else

                if ((args.Parameters.Count == 0) || (args.Parameters[0] != "red") || (args.Parameters[0] != "blue"))
                {
                    player.SendErrorMessage("Usage: /cw join <blue|red>");
                    return;
                }
                int index = _arenas.FindIndex(delegate (Arena arena) { return arena.name.ToLower() == GameInProgress; });
                if (args.Parameters[0] == "red")
                {
                    player.TPlayer.team = 1;
                    redTeam.Add(player);
                    player.Teleport(_arenas[index].rSpawn.X, _arenas[index].rSpawn.Y);
                    player.SendMessage("You have joined the red team.", Color.LimeGreen);
                    return;
                }
                if (args.Parameters[0] == "blue")
                {
                    player.TPlayer.team = 3;
                    blueTeam.Add(player);
                    player.Teleport(_arenas[index].bSpawn.X, _arenas[index].bSpawn.Y);
                    player.SendMessage("You have joined the blue team.", Color.LimeGreen);
                    return;
                }
                return;
            }
            #endregion

            #region score
            if (action == "score")
            {
                if (GameInProgress == "none")
                {
                    player.SendErrorMessage("No game running!");
                    return;
                }
                ScoreUpdate();
                return;
            }
            #endregion

            #region catch
            player.SendErrorMessage("Aliases: /classwars, /cw");
            if (player.HasPermission("cw.start"))
                player.SendErrorMessage("/cw start <arena>");
            if (player.HasPermission("cw.add"))
            {
                player.SendErrorMessage("/cw add <arena>");
                player.SendErrorMessage("/cw set <arena> <host|redspawn|bluespawn|arenaTopLeft|arenaBottomRight|switch>");
            }
            if (player.HasPermission("cw.del"))
                player.SendErrorMessage("/cw del <arena>");
            if (player.HasPermission("tshock.admin.warp"))
                player.SendErrorMessage("/cw goto <arena>");
            player.SendErrorMessage("/cw list");
            return;
            #endregion
        }

        /*private void cwstats(CommandArgs args)
        {
            var player = args.Player;
            if (!playerStatExists(player.User.Name))
            {
                player.SendErrorMessage("No stats for this account");
                return;
            }
            var playerstat = getStats(player.User.Name);
            if ((args.Parameters.Count == 0) || (args.Parameters[1] == "all"))
            {
                player.SendMessage("Statistics for " + player.User.Name + ":", Color.LimeGreen);
                player.SendMessage("KDA: " + playerstat.kda().ToString(), Color.LimeGreen);
                player.SendMessage("Win/Loss: " + playerstat.winLossRatio().ToString(), Color.LimeGreen);
                player.SendMessage("Games Played: " + playerstat.gamesCompleted, Color.LimeGreen);
                player.SendMessage("Average Game Time: " + playerstat.averageGameTime().ToString(), Color.LimeGreen);
                return;
            }
            var stat = args.Parameters[0].ToLower();
            if (stat == "kills")
            {
                player.SendMessage("Kills: " + playerstat.kills, Color.LimeGreen);
                return;
            }
            if (stat == "deaths")
            {
                player.SendMessage("Deaths: " + playerstat.deaths, Color.LimeGreen);
                return;
            }
            if (stat == "kda")
            {
                player.SendMessage("KDA: " + playerstat.kda().ToString(), Color.LimeGreen);
                return;
            }
            if (stat == "games")
            {
                player.SendMessage("Games Played: " + playerstat.gamesCompleted, Color.LimeGreen);
                return;
            }
            if (stat == "averagetime")
            {
                player.SendMessage("Average Game Time: " + playerstat.averageGameTime().ToString(), Color.LimeGreen);
                return;
            }
            if (stat == "time")
            {
                player.SendMessage("Total time played: " + (playerstat.timePlayed + playerstat.timeIncomplete), Color.LimeGreen);
                return;
            }
            player.SendErrorMessage("/cwstats <all|kills|deaths|KDA|gamesplayed|averagetime|time>");
            return;
        }*/
        #endregion
    }
}
