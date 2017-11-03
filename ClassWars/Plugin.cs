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
using OTAPI;
using System.IO.Streams;
using System.Threading;
using System.Timers;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Terraria.Localization;

namespace ClassWars
{
    [ApiVersion(2, 1)]
    public class ClassWars : TerrariaPlugin
    {
        public override string Name { get { return "ClassWars"; } }
        public override string Author { get { return "Alec"; } }
        public override string Description { get { return "Automatic Class Wars hosting plugin."; } }
        public override Version Version { get { return new Version(2, 0, 0, 0); } }

        #region variables
        public static Dictionary<string, int> Colors = new Dictionary<string, int>();
        private static Database arena_db;
        private static ClassDatabase class_db;
        private static List<Classvar> classes = new List<Classvar>();
        private static List<Arena> _arenas = new List<Arena>();
        private static List<string> arenaNames = new List<string>();
        private List<ProgressiveItemBuff> pItemBuff = new List<ProgressiveItemBuff>();
        private List<ProgressiveBuff> pBuff = new List<ProgressiveBuff>();
        private List<ProgressiveAmmo> pAmmo = new List<ProgressiveAmmo>();
        private List<TempClassStorage> gameClasses = new List<TempClassStorage>();
        private List<TempClassStorage> tempClasses = new List<TempClassStorage>();
        private string GameInProgress = "none";
        private List<TSPlayer> redTeam = new List<TSPlayer>() { null };
        private List<TSPlayer> blueTeam = new List<TSPlayer>() { null };
        public int redPaintID, bluePaintID, blueBunkerCount, redBunkerCount, arenaIndex;
        public static System.Timers.Timer scoreCheck = new System.Timers.Timer { Interval = 120000, AutoReset = true, Enabled = false};
        public static System.Timers.Timer countdown = new System.Timers.Timer { Interval = 1000, AutoReset = true, Enabled = false };
        public static System.Timers.Timer gameTick = new System.Timers.Timer { Interval = 1000, AutoReset = true, Enabled = true };
        private int count, redDeathCount, blueDeathCount;
        public DateTime start, end;
        #endregion
        public ClassWars(Main game) : base(game)
        {
            Order = 10;
        }

        public override void Initialize()
        {
            arena_db = Database.InitDb("CWArenas");
            arena_db.LoadArenas(ref _arenas);
            class_db = ClassDatabase.InitDb("Classes");
            class_db.LoadClasses(ref classes);
            arenaNameReload();
            GameInProgress = "none";
            ServerApi.Hooks.NetGetData.Register(this, onGetData);
            ServerApi.Hooks.ServerLeave.Register(this, OnPlayerLeave);
            ServerApi.Hooks.GameUpdate.Register(this, onUpdate);
            ServerApi.Hooks.NetGreetPlayer.Register(this, onGreet);
            Commands.ChatCommands.Add(new Command("cw.main", cw, "cw", "classwars"));
            Commands.ChatCommands.Add(new Command("CS.main", cselect, "class", "cs"));
            scoreCheck.Elapsed += ScoreUpdate;
            countdown.Elapsed += startCountdown;
            gameTick.Elapsed += tickClasses;

            Colors.Add("blank", 0);

            Main.player[Main.myPlayer] = new Player();
            var item = new Item();
            for (int i = -48; i < Main.maxItemTypes; i++)
            {
                item.netDefaults(i);
                if (item.paint > 0)
                    Colors.Add(item.Name.Substring(0, item.Name.Length - 6).ToLowerInvariant(), item.paint);
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
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, onGreet);
            }
            base.Dispose(disposing);
        }

        #region triggers
        private void onGetData(GetDataEventArgs args)
        {
            if (GameInProgress == "none")
                return;
            if (args.MsgID == PacketTypes.Tile)
                using (var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
                {
                    int action = data.ReadByte();
                    int blockX = data.ReadInt16();
                    int blockY = data.ReadInt16();
                    if (action == 0)
                    {
                        CheckWins();
                        Task.Delay(1000).ContinueWith(t => CheckWins());
                        if (Main.tile[blockX, blockY].type != 203 && Main.tile[blockX, blockY].type != 25 && Main.tile[blockX, blockY].type != 117)
                        {
                            if (isMiner(args.Msg.whoAmI))
                            {
                                if (blueTeam.Contains(TShock.Players[args.Msg.whoAmI]) || redTeam.Contains(TShock.Players[args.Msg.whoAmI]))
                                {
                                    args.Handled = true;
                                    TShock.Players[args.Msg.whoAmI].SendTileSquare(blockX, blockY, 4);
                                    return;
                                }
                            }
                        }
                    }
                }
            if (args.MsgID == PacketTypes.PlayerSpawn)
            {
                if (redTeam.Contains(TShock.Players[args.Msg.whoAmI]))
                {
                    TShock.Players[args.Msg.whoAmI].Teleport(_arenas[arenaIndex].rSpawn.X * 16, _arenas[arenaIndex].rSpawn.Y * 16);
                    TShock.Players[args.Msg.whoAmI].Heal(600);
                    redDeathCount = redDeathCount + 1;
                    return;
                }
                if (blueTeam.Contains(TShock.Players[args.Msg.whoAmI]))
                {
                    TShock.Players[args.Msg.whoAmI].Teleport(_arenas[arenaIndex].bSpawn.X * 16, _arenas[arenaIndex].bSpawn.Y * 16);
                    TShock.Players[args.Msg.whoAmI].Heal(600);
                    blueDeathCount = blueDeathCount + 1;
                    return;
                }
            }

        }

        private void onGreet(GreetPlayerEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (GameInProgress != "none")
            {
                foreach(TempClassStorage c in tempClasses)
                {
                    if (c.player == player)
                    {
                        setclass(player, c.tempClass.name);
                        player.SendInfoMessage("Do \"/cw join [team]\" to rejoin your team");
                    }
                }
            }
        }
        
        private void OnPlayerLeave(LeaveEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (redTeam.Contains(player))
            {
                redTeam.Remove(player);
            }
            if (blueTeam.Contains(player))
            {
                blueTeam.Remove(player);
            }
            
            resetClass(player);

        }

        private void onUpdate(EventArgs args)
        {
            if (GameInProgress != "none")
            {
                foreach (TSPlayer plr in redTeam)
                {
                    //Forces PVP for both teams for the duration of the game.
                    plr.TPlayer.hostile = true;
                    NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, null, plr.Index, 0f, 0f, 0f);
                }
                foreach (TSPlayer plr in blueTeam)
                {
                    plr.TPlayer.hostile = true;
                    NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, null, plr.Index, 0f, 0f, 0f);
                }
            }
        }
        #endregion

        private bool isMiner(int whoAmI)
        {
            TSPlayer player = TShock.Players[whoAmI];
            for (int i = 0; i < 20; i++)
            {
                if (player.TPlayer.armor[i].type == 410)
                    return true;
            }
            return false;
        }

        private void arenaNameReload()
        {
            arenaNames.Clear();
            foreach (Arena arena in _arenas)
            {
                arenaNames.Add(arena.name.ToLower());
            }
        }

        #region classHandling
        public void setclass(TSPlayer pl, string className)
        {
            classes.Clear();
            class_db.LoadClasses(ref classes);
            TSPlayer player = TShock.Players[pl.Index];
            PlayerInfo info = player.GetPlayerInfo();
            List<Classvar> z = ClassInfo.ClassLookup(className, classes);
            if (z.Count > 1)
            {
                List<string> names = new List<string>();
                foreach(Classvar a in z)
                {
                    names.Add(a.name);
                }
                string y = string.Join(", ", names);
                player.SendErrorMessage("Multiple classes found: ");
                player.SendErrorMessage(y);
                return;
            }
            if (z.Count == 0)
            {
                player.SendErrorMessage("No classes found.");
                return;
            }
            Classvar c = z[0];
            resetClass(player);

            if (c == null)
            {
                player.SendErrorMessage("Class " + className + " not found.");
                return;
            }
            if (info.backup == null)
            {
                info.backup = new PlayerData(player);
                info.backup.CopyCharacter(player);
            }
            player.IgnoreSSCPackets = true;
            player.PlayerData.health = c.maxHealth;
            player.PlayerData.maxHealth = c.maxHealth;
            player.TPlayer.statLifeMax2 = c.maxHealth;
            player.TPlayer.statLifeMax = c.maxHealth;
            player.TPlayer.statLife = c.maxHealth;
            player.TPlayer.statManaMax = c.maxMana;
            player.TPlayer.statMana = c.maxMana;
            if (c.extraSlot != null)
                player.TPlayer.extraAccessory = c.extraSlot.Value == 1 ? true : false;

            #region invCopy
            for (int i = 0; i < NetItem.MaxInventory; i++)
            {
                if (i < NetItem.InventoryIndex.Item2)
                {
                    //0-58
                    player.TPlayer.inventory[i].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.inventory[i].netID != 0)
                    {
                        player.TPlayer.inventory[i].stack = c.inventory[i].Stack;
                        player.TPlayer.inventory[i].prefix = c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.ArmorIndex.Item2)
                {
                    //59-78
                    var index = i - NetItem.ArmorIndex.Item1;
                    player.TPlayer.armor[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.armor[index].netID != 0)
                    {
                        player.TPlayer.armor[index].stack = c.inventory[i].Stack;
                        player.TPlayer.armor[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.DyeIndex.Item2)
                {
                    //79-88
                    var index = i - NetItem.DyeIndex.Item1;
                    player.TPlayer.dye[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.dye[index].netID != 0)
                    {
                        player.TPlayer.dye[index].stack = c.inventory[i].Stack;
                        player.TPlayer.dye[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.MiscEquipIndex.Item2)
                {
                    //89-93
                    var index = i - NetItem.MiscEquipIndex.Item1;
                    player.TPlayer.miscEquips[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.miscEquips[index].netID != 0)
                    {
                        player.TPlayer.miscEquips[index].stack = c.inventory[i].Stack;
                        player.TPlayer.miscEquips[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.MiscDyeIndex.Item2)
                {
                    //93-98
                    var index = i - NetItem.MiscDyeIndex.Item1;
                    player.TPlayer.miscDyes[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.miscDyes[index].netID != 0)
                    {
                        player.TPlayer.miscDyes[index].stack = c.inventory[i].Stack;
                        player.TPlayer.miscDyes[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.PiggyIndex.Item2)
                {
                    //98-138
                    var index = i - NetItem.PiggyIndex.Item1;
                    player.TPlayer.bank.item[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.bank.item[index].netID != 0)
                    {
                        player.TPlayer.bank.item[index].stack = c.inventory[i].Stack;
                        player.TPlayer.bank.item[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.SafeIndex.Item2)
                {
                    //138-178
                    var index = i - NetItem.SafeIndex.Item1;
                    player.TPlayer.bank2.item[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.bank2.item[index].netID != 0)
                    {
                        player.TPlayer.bank2.item[index].stack = c.inventory[i].Stack;
                        player.TPlayer.bank2.item[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.TrashIndex.Item2)
                {
                    //179-219
                    var index = i - NetItem.TrashIndex.Item1;
                    player.TPlayer.trashItem.netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.trashItem.netID != 0)
                    {
                        player.TPlayer.trashItem.stack = c.inventory[i].Stack;
                        player.TPlayer.trashItem.prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else
                {
                    //220
                    var index = i - NetItem.ForgeIndex.Item1;
                    player.TPlayer.bank3.item[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.bank3.item[index].netID != 0)
                    {
                        player.TPlayer.bank3.item[index].stack = c.inventory[i].Stack;
                        player.TPlayer.bank3.item[index].Prefix((byte)c.inventory[i].PrefixId);
                    }

                }
            }

            float slot = 0f;
            for (int k = 0; k < NetItem.InventorySlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].inventory[k].Name), player.Index, slot, (float)Main.player[player.Index].inventory[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.ArmorSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].armor[k].Name), player.Index, slot, (float)Main.player[player.Index].armor[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.DyeSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].dye[k].Name), player.Index, slot, (float)Main.player[player.Index].dye[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.MiscEquipSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].miscEquips[k].Name), player.Index, slot, (float)Main.player[player.Index].miscEquips[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.MiscDyeSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].miscDyes[k].Name), player.Index, slot, (float)Main.player[player.Index].miscDyes[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.PiggySlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].bank.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank.item[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.SafeSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].bank2.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank2.item[k].prefix);
                slot++;
            }
            NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].trashItem.Name), player.Index, slot++, (float)Main.player[player.Index].trashItem.prefix);
            for (int k = 0; k < NetItem.ForgeSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].bank3.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank3.item[k].prefix);
                slot++;
            }


            NetMessage.SendData(4, -1, -1, NetworkText.FromLiteral(player.Name), player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(42, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(16, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

            slot = 0f;
            for (int k = 0; k < NetItem.InventorySlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].inventory[k].Name), player.Index, slot, (float)Main.player[player.Index].inventory[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.ArmorSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].armor[k].Name), player.Index, slot, (float)Main.player[player.Index].armor[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.DyeSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].dye[k].Name), player.Index, slot, (float)Main.player[player.Index].dye[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.MiscEquipSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].miscEquips[k].Name), player.Index, slot, (float)Main.player[player.Index].miscEquips[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.MiscDyeSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].miscDyes[k].Name), player.Index, slot, (float)Main.player[player.Index].miscDyes[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.PiggySlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].bank.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank.item[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.SafeSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].bank2.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank2.item[k].prefix);
                slot++;
            }
            NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].trashItem.Name), player.Index, slot++, (float)Main.player[player.Index].trashItem.prefix);
            for (int k = 0; k < NetItem.ForgeSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].bank3.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank3.item[k].prefix);
                slot++;
            }



            NetMessage.SendData(4, player.Index, -1, NetworkText.FromLiteral(player.Name), player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(42, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(16, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

            for (int k = 0; k < 22; k++)
            {
                player.TPlayer.buffType[k] = 0;
            }

            /*
			 * The following packets are sent twice because the server will not send a packet to a client
			 * if they have not spawned yet if the remoteclient is -1
			 * This is for when players login via uuid or serverpassword instead of via
			 * the login command.
			 */
            NetMessage.SendData(50, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(50, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

            NetMessage.SendData(76, player.Index, -1, NetworkText.Empty, player.Index);
            NetMessage.SendData(76, -1, -1, NetworkText.Empty, player.Index);

            NetMessage.SendData(39, player.Index, -1, NetworkText.Empty, 400);
            #endregion

            info.classInfo = c;
            info.preview = false;
            player.SendInfoMessage(c.name + " selected.");
            TempClassStorage x = new TempClassStorage(c, player);
            player.IgnoreSSCPackets = false;
            foreach(Ammo a in c.ammo.ToArray())
            {
                pAmmo.Add(new ProgressiveAmmo(a.refresh, a.item, a.quantity, a.maxCount, a.prefix, 0, player));
            }
            foreach (Buff b in c.buffs.ToArray())
            {
                pBuff.Add(new ProgressiveBuff(b.id, b.duration, 0, player));
            }
            foreach (ItemBuff i in c.itembuffs.ToArray())
            {
                pItemBuff.Add(new ProgressiveItemBuff(i.id, i.duration, i.item, 0, player));
            }
            gameClasses.Add(x);
        }

        public void previewclass(TSPlayer pl, string className)
        {
            classes.Clear();
            class_db.LoadClasses(ref classes);
            TSPlayer player = TShock.Players[pl.Index];
            PlayerInfo info = player.GetPlayerInfo();
            List<Classvar> z = ClassInfo.ClassLookup(className, classes);
            if (z.Count > 1)
            {
                List<string> names = new List<string>();
                foreach (Classvar a in z)
                {
                    names.Add(a.name);
                }
                string y = string.Join(", ", names);
                player.SendErrorMessage("Multiple classes found: ");
                player.SendErrorMessage(y);
                return;
            }
            if (z.Count == 0)
            {
                player.SendErrorMessage("No classes found.");
            }
            Classvar c = z[0];
            resetClass(player);
            if (info.backup == null)
            {
                info.backup = new PlayerData(player);
                info.backup.CopyCharacter(player);
            }
            info.preview = true;
            //player.PlayerData.inventory = c.inventory;
            player.IgnoreSSCPackets = true;
            player.PlayerData.health = c.maxHealth;
            player.PlayerData.maxHealth = c.maxHealth;
            player.TPlayer.statLifeMax2 = c.maxHealth;
            player.TPlayer.statLifeMax = c.maxHealth;
            player.TPlayer.statLife = c.maxHealth;
            player.TPlayer.statManaMax = c.maxMana;
            player.TPlayer.statMana = c.maxMana;
            if (c.extraSlot != null)
                player.TPlayer.extraAccessory = c.extraSlot.Value == 1 ? true : false;

            #region invCopy
            for (int i = 0; i < NetItem.MaxInventory; i++)
            {
                if (i < NetItem.InventoryIndex.Item2)
                {
                    //0-58
                    player.TPlayer.inventory[i].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.inventory[i].netID != 0)
                    {
                        player.TPlayer.inventory[i].stack = c.inventory[i].Stack;
                        player.TPlayer.inventory[i].prefix = c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.ArmorIndex.Item2)
                {
                    //59-78
                    var index = i - NetItem.ArmorIndex.Item1;
                    player.TPlayer.armor[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.armor[index].netID != 0)
                    {
                        player.TPlayer.armor[index].stack = c.inventory[i].Stack;
                        player.TPlayer.armor[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.DyeIndex.Item2)
                {
                    //79-88
                    var index = i - NetItem.DyeIndex.Item1;
                    player.TPlayer.dye[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.dye[index].netID != 0)
                    {
                        player.TPlayer.dye[index].stack = c.inventory[i].Stack;
                        player.TPlayer.dye[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.MiscEquipIndex.Item2)
                {
                    //89-93
                    var index = i - NetItem.MiscEquipIndex.Item1;
                    player.TPlayer.miscEquips[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.miscEquips[index].netID != 0)
                    {
                        player.TPlayer.miscEquips[index].stack = c.inventory[i].Stack;
                        player.TPlayer.miscEquips[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.MiscDyeIndex.Item2)
                {
                    //93-98
                    var index = i - NetItem.MiscDyeIndex.Item1;
                    player.TPlayer.miscDyes[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.miscDyes[index].netID != 0)
                    {
                        player.TPlayer.miscDyes[index].stack = c.inventory[i].Stack;
                        player.TPlayer.miscDyes[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.PiggyIndex.Item2)
                {
                    //98-138
                    var index = i - NetItem.PiggyIndex.Item1;
                    player.TPlayer.bank.item[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.bank.item[index].netID != 0)
                    {
                        player.TPlayer.bank.item[index].stack = c.inventory[i].Stack;
                        player.TPlayer.bank.item[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.SafeIndex.Item2)
                {
                    //138-178
                    var index = i - NetItem.SafeIndex.Item1;
                    player.TPlayer.bank2.item[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.bank2.item[index].netID != 0)
                    {
                        player.TPlayer.bank2.item[index].stack = c.inventory[i].Stack;
                        player.TPlayer.bank2.item[index].prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else if (i < NetItem.TrashIndex.Item2)
                {
                    //179-219
                    var index = i - NetItem.TrashIndex.Item1;
                    player.TPlayer.trashItem.netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.trashItem.netID != 0)
                    {
                        player.TPlayer.trashItem.stack = c.inventory[i].Stack;
                        player.TPlayer.trashItem.prefix = (byte)c.inventory[i].PrefixId;
                    }
                }
                else
                {
                    //220
                    var index = i - NetItem.ForgeIndex.Item1;
                    player.TPlayer.bank3.item[index].netDefaults(c.inventory[i].NetId);

                    if (player.TPlayer.bank3.item[index].netID != 0)
                    {
                        player.TPlayer.bank3.item[index].stack = c.inventory[i].Stack;
                        player.TPlayer.bank3.item[index].Prefix((byte)c.inventory[i].PrefixId);
                    }

                }
            }

            float slot = 0f;
            for (int k = 0; k < NetItem.InventorySlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].inventory[k].Name), player.Index, slot, (float)Main.player[player.Index].inventory[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.ArmorSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].armor[k].Name), player.Index, slot, (float)Main.player[player.Index].armor[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.DyeSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].dye[k].Name), player.Index, slot, (float)Main.player[player.Index].dye[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.MiscEquipSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].miscEquips[k].Name), player.Index, slot, (float)Main.player[player.Index].miscEquips[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.MiscDyeSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].miscDyes[k].Name), player.Index, slot, (float)Main.player[player.Index].miscDyes[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.PiggySlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].bank.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank.item[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.SafeSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].bank2.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank2.item[k].prefix);
                slot++;
            }
            NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].trashItem.Name), player.Index, slot++, (float)Main.player[player.Index].trashItem.prefix);
            for (int k = 0; k < NetItem.ForgeSlots; k++)
            {
                NetMessage.SendData(5, -1, -1, NetworkText.FromLiteral(Main.player[player.Index].bank3.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank3.item[k].prefix);
                slot++;
            }


            NetMessage.SendData(4, -1, -1, NetworkText.FromLiteral(player.Name), player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(42, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(16, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

            slot = 0f;
            for (int k = 0; k < NetItem.InventorySlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].inventory[k].Name), player.Index, slot, (float)Main.player[player.Index].inventory[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.ArmorSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].armor[k].Name), player.Index, slot, (float)Main.player[player.Index].armor[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.DyeSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].dye[k].Name), player.Index, slot, (float)Main.player[player.Index].dye[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.MiscEquipSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].miscEquips[k].Name), player.Index, slot, (float)Main.player[player.Index].miscEquips[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.MiscDyeSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].miscDyes[k].Name), player.Index, slot, (float)Main.player[player.Index].miscDyes[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.PiggySlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].bank.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank.item[k].prefix);
                slot++;
            }
            for (int k = 0; k < NetItem.SafeSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].bank2.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank2.item[k].prefix);
                slot++;
            }
            NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].trashItem.Name), player.Index, slot++, (float)Main.player[player.Index].trashItem.prefix);
            for (int k = 0; k < NetItem.ForgeSlots; k++)
            {
                NetMessage.SendData(5, player.Index, -1, NetworkText.FromLiteral(Main.player[player.Index].bank3.item[k].Name), player.Index, slot, (float)Main.player[player.Index].bank3.item[k].prefix);
                slot++;
            }



            NetMessage.SendData(4, player.Index, -1, NetworkText.FromLiteral(player.Name), player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(42, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(16, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

            for (int k = 0; k < 22; k++)
            {
                player.TPlayer.buffType[k] = 0;
            }

            /*
			 * The following packets are sent twice because the server will not send a packet to a client
			 * if they have not spawned yet if the remoteclient is -1
			 * This is for when players login via uuid or serverpassword instead of via
			 * the login command.
			 */
            NetMessage.SendData(50, -1, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);
            NetMessage.SendData(50, player.Index, -1, NetworkText.Empty, player.Index, 0f, 0f, 0f, 0);

            NetMessage.SendData(76, player.Index, -1, NetworkText.Empty, player.Index);
            NetMessage.SendData(76, -1, -1, NetworkText.Empty, player.Index);

            NetMessage.SendData(39, player.Index, -1, NetworkText.Empty, 400);
        #endregion

            info.classInfo = c;

            player.SendInfoMessage("Previewing " + c.name + ".");
            player.SendInfoMessage("Inventory will revert on /class select, /class preview, or in 20 seconds.");
            Task.Delay(20000).ContinueWith(t => resetClassPreview(player));
        }

        public void resetClassPreview(TSPlayer pl)
        {
            TSPlayer player = TShock.Players[pl.Index];
            PlayerInfo info = player.GetPlayerInfo();
            if (player.Dead)
            {
                Task.Delay(1000).ContinueWith(t => resetClassPreview(player));
                return;
            }
            if (info.preview == false)
            {
                return;
            }
            else
            {
                player.SendInfoMessage("Preview automatically ended.");
            }
            if (info.Restore(player))
            {
                player.SendInfoMessage("Inventory restored.");
            }
            else
            {
                player.SendErrorMessage("Unable to restore inventory.");
            }
            info.preview = false;
            #region listRemoval
            foreach (ProgressiveAmmo ammo in pAmmo.ToArray())
            {
                if (ammo.player == player)
                {
                    pAmmo.Remove(ammo);
                }
            }
            foreach (ProgressiveItemBuff iBuff in pItemBuff.ToArray())
            {
                if (iBuff.player == player)
                {
                    pItemBuff.Remove(iBuff);
                }
            }
            foreach (ProgressiveBuff _buff in pBuff.ToArray())
            {
                if (_buff.player == player)
                {
                    pBuff.Remove(_buff);
                }
            }
            #endregion
        }

        public void resetClass(TSPlayer pl)
        {
            TSPlayer player = TShock.Players[pl.Index];
            PlayerInfo info = player.GetPlayerInfo();
            if (player.Dead)
            {
                Task.Delay(1000).ContinueWith(t => resetClass(player));
                return;
            }
            if (info.preview == false)
            {
                foreach(TempClassStorage t in gameClasses.ToArray())
                {
                    if (t.player == player)
                    {
                        tempClasses.Add(t);
                        gameClasses.Remove(t);
                    }
                }
            }
            else
            {
                player.SendInfoMessage("Preview automatically ended.");
            }
            if (info.Restore(player))
            {
                player.SendInfoMessage("Inventory restored.");
            }
            else
            {
                player.SendErrorMessage("Unable to restore inventory.");
            }
            info.preview = false;
            #region listRemoval
            foreach(ProgressiveAmmo ammo in pAmmo.ToArray())
            {
                if (ammo.player == player)
                {
                    pAmmo.Remove(ammo);
                }
            }
            foreach(ProgressiveItemBuff iBuff in pItemBuff.ToArray())
            {
                if (iBuff.player == player)
                {
                    pItemBuff.Remove(iBuff);
                }
            }
            foreach(ProgressiveBuff _buff in pBuff.ToArray())
            {
                if (_buff.player == player)
                {
                    pBuff.Remove(_buff);
                }
            }
#endregion
        }
        #endregion

        #region gameHandling
        private void gameStart()
        {
            if (GameInProgress != "none")
            {
                return;
            }
            redDeathCount = 0;
            blueDeathCount = 0;
            Arena arena = _arenas[arenaIndex];
            GameInProgress = arena.name;
            redTeam.Clear();
            blueTeam.Clear();
            foreach (TSPlayer player in TShock.Players.Where(player => player != null))
            {
                if (Main.player[player.Index].team == 1)
                {
                    if (redTeam == null)
                        redTeam = new List<TSPlayer> { player };
                    else
                        redTeam.Add(player);
                }
                if (Main.player[player.Index].team == 3)
                {
                    if (blueTeam == null)
                        blueTeam = new List<TSPlayer> { player };
                    else
                        blueTeam.Add(player);
                }
            }
            if (redTeam.Count() < 1)
            {
                TShock.Utils.Broadcast("Red team is empty.", Color.Red);
                GameInProgress = "none";
                return;
            }
            if (blueTeam.Count() < 1)
            {
                TShock.Utils.Broadcast("Blue team is empty.", Color.Red);
                GameInProgress = "none";
                return;
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
            count = 5;
            countdown.Enabled = true;
            scoreCheck.Enabled = true;
        }

        public void startCountdown(object sender, ElapsedEventArgs e)
        {
            Arena arena = _arenas[arenaIndex];
            if (count > 0)
                TShock.Utils.Broadcast("CW Game starting in: " + count.ToString(), Color.Yellow);
            if (count < 0)
            {
                countdown.Enabled = false;
                return;
            }
            if (count == 0)
            {
                count--;
                TShock.Utils.Broadcast("LET THE GAMES BEGIN!", Color.Yellow);
                start = DateTime.Now;
                countdown.Enabled = false;
                return;
            }
            count--;
            foreach (TSPlayer player in redTeam)
            {
                player.Teleport(arena.rSpawn.X * 16, arena.rSpawn.Y * 16);
            }
            foreach (TSPlayer player in blueTeam)
            {
                player.Teleport(arena.bSpawn.X * 16, arena.bSpawn.Y * 16);
            }
        }

        public void ScoreUpdate(object sender, ElapsedEventArgs e)
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
                            blueBunkerCount = blueBunkerCount + 1;
                        if (color == redPaintID)
                            redBunkerCount = redBunkerCount + 1;
                    }
                }
            }
            TShock.Utils.Broadcast("Red team's bunker has " + redBunkerCount.ToString() + " blocks remaining.", Color.Red);
            TShock.Utils.Broadcast("Blue team's bunker has " + blueBunkerCount.ToString() + " blocks remaining.", Color.LightBlue);
        }

        public void tickClasses(object sender, ElapsedEventArgs e)
        {
            foreach (ProgressiveBuff b in pBuff.ToArray())
            {
                buffPlayer(b.player, b.id, b.duration);
            }
            foreach (ProgressiveItemBuff i in pItemBuff.ToArray())
            {
                if (i.player.TPlayer.inventory[i.player.TPlayer.selectedItem].Name == TShock.Utils.GetItemById(i.item).Name)
                {
                    buffPlayer(i.player, i.id, i.duration);
                }
            }
            foreach (ProgressiveAmmo a in pAmmo.ToArray())
            {
                Item i = TShock.Utils.GetItemById(a.item);
                if (a.count <= 0)
                {
                    int stackSize = ItemCount(a.player, i);
                    if (stackSize < a.maxCount)
                    {
                        if (a.player.InventorySlotAvailable || (i.type > 70 && i.type < 75) || i.ammo > 0 || i.type == 58 || i.type == 184 && a.quantity != 0)
                        {
                            a.player.GiveItem(i.type, i.Name, i.width, i.height, a.quantity, a.prefix); 
                        }
                    }
                }
                else
                {
                    count--;
                }
            }
        }

        public int ItemCount(TSPlayer player, Item _item)
        {
            int count = 0;
            for (int i = 0; i < 59; i++)
            {
                if (player.TPlayer.inventory[i].Name == _item.Name)
                    count = count + player.TPlayer.inventory[i].stack;
            }
            for (int i = 0; i < 20; i++)
            {
                if (player.TPlayer.armor[i].Name == _item.Name)
                    count = count + player.TPlayer.inventory[i].stack;
            }
            if (player.TPlayer.trashItem.Name == _item.Name)
                count = count + player.TPlayer.trashItem.stack;
            return count;
        }
        
        public void buffPlayer(TSPlayer player, int buffID, int duration)
        {
            if (buffID == 94)
            {
                player.TPlayer.ClearBuff(94);
                player.SetBuff(buffID, duration * 60);
                return;
            }
            player.SetBuff(buffID, duration * 60);
            return;
        }

        public void Refill()
        {
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
                        {
                            blueBunkerCount = 1;
                        }
                        if (color == redPaintID)
                        {
                            redBunkerCount = 1;
                        }
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
            end = DateTime.Now;
            TimeSpan elapsed = end - start;
            Arena arena = _arenas[arenaIndex];
            GameInProgress = "none";
            scoreCheck.Enabled = false;
            foreach (TSPlayer player in redTeam)
            {
                player.Teleport(4374 * 16, 240 * 16);
                player.TPlayer.hostile = false;
                NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, null, player.Index, 0f, 0f, 0f);
                resetClass(player);
            }
            foreach (TSPlayer player in blueTeam)
            {
                player.Teleport(4374 * 16, 240 * 16);
                player.TPlayer.hostile = false;
                NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, null, player.Index, 0f, 0f, 0f);
                resetClass(player);
            }
            redTeam.Clear();
            blueTeam.Clear();
            Color winningTeam = new Color();
            if (winner)
                winningTeam = Color.HotPink;
            else
                winningTeam = Color.LightBlue;
            TShock.Utils.Broadcast("=====================", winningTeam);
            if (winner)
                TShock.Utils.Broadcast("Red Team Wins!", winningTeam);
            else
                TShock.Utils.Broadcast("Blue Team Win!", winningTeam);
            TShock.Utils.Broadcast("=====================", winningTeam);
            TShock.Utils.Broadcast("Red Team Deaths: " + redDeathCount.ToString(), winningTeam);
            TShock.Utils.Broadcast("Blue Team Deaths: " + blueDeathCount.ToString(), winningTeam);
            TShock.Utils.Broadcast("Total Game Time: " + elapsed.Hours + " Hours, " + elapsed.Minutes + " Minutes, " + elapsed.Seconds+ " Seconds.", winningTeam);
            TShock.Utils.Broadcast("=====================", winningTeam);
            Refill();
            tempClasses.Clear();
            gameClasses.Clear();
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
                foreach (Arena arena in _arenas.ToArray())
                {
                    if (arena.name == name)
                    {
                        _arenas.Remove(arena);
                        player.SendMessage("Arena " + arena.name + "removed.", Color.LimeGreen);
                        return;
                    }
                }
                player.SendErrorMessage("dafuq #372");
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
                    player.SendErrorMessage("Arena " + arenaName + " not found.");
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
                    player.SendMessage(_arenas[index].name + " host location set to your position.", Color.LimeGreen);
                    return;
                }

                #endregion

                #region redspawn
                if (action == "redspawn")
                {
                    _arenas[index].rSpawn = new Vector2(args.Player.TileX, args.Player.TileY);
                    arena_db.UpdateArena(_arenas[index]);
                    player.SendMessage(_arenas[index].name + " red spawn location set to your position.", Color.LimeGreen);
                    return;
                }
                #endregion

                #region bluespawn
                if (action == "bluespawn")
                {
                    _arenas[index].bSpawn = new Vector2 (args.Player.TileX, args.Player.TileY);
                    arena_db.UpdateArena(_arenas[index]);
                    player.SendMessage(_arenas[index].name + " blue spawn location set to your position.", Color.LimeGreen);
                    return;
                }
                #endregion

                #region arenaBoundaries
                if ((action == "arenabounds") || (action == "arenaboundaries") || (action == "ab"))
                {
                    if (args.Parameters.Count == 0)
                    {
                        player.SendErrorMessage("Usage:");
                        player.SendErrorMessage("/cw set [arena] arenabounds <1/2>");
                        player.SendErrorMessage("/cw set [arena] arenabounds define");
                        return;
                    }
                    if (args.Parameters[0].ToLower() == "define")
                    {
                        Vector2 topLeft = new Vector2((Math.Min(player.TempPoints[0].X, player.TempPoints[1].X)), (Math.Min(player.TempPoints[0].Y, player.TempPoints[1].Y)));
                        Vector2 bottomRight = new Vector2((Math.Max(player.TempPoints[0].X, player.TempPoints[1].X)), (Math.Max(player.TempPoints[0].Y, player.TempPoints[1].Y)));
                        _arenas[index].arenaTopL = topLeft;
                        _arenas[index].arenaBottomR = bottomRight;
                        arena_db.UpdateArena(_arenas[index]);
                        player.SendMessage("Arena boundaries defined for "+ _arenas[index].name + ".", Color.LimeGreen);
                        return;
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
                    _arenas[index].switchPos = new Vector2(args.Player.TileX, args.Player.TileY);
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
                    if (GameInProgress == "none")
                    {
                        player.SendErrorMessage("No game currently running.");
                        return;
                    }
                    GameInProgress = "none";
                    scoreCheck.Enabled = false;
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
                if (args.Parameters.Count == 0)
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
                player.SendErrorMessage("Usage: /cw join <blue|red>");
                return;            }
            #endregion

            #region score
            if (action == "score")
            {
                if (GameInProgress == "none")
                {
                    player.SendErrorMessage("No game running!");
                    return;
                }
                ScoreUpdate(null, null);
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

        private void cselect(CommandArgs args)
        {
            TSPlayer player = args.Player;

            #region help
            if (args.Parameters.Count == 0 || (args.Parameters[0] == "help" && (args.Parameters.Count != 2 || args.Parameters[1] != "admin")))
            {
                player.SendErrorMessage("Usage: /class select [name]");
                player.SendErrorMessage("/class list [category]");
                player.SendErrorMessage("/class preview [name]");
                player.SendErrorMessage("/class desc [name]");
                if (player.HasPermission("CS.admin"))
                {
                    player.SendErrorMessage("/class help admin");
                }
                return;
            }
            if (args.Parameters[0] == "help" && args.Parameters[1] == "admin")
            {
                if (player.HasPermission("CS.admin"))
                {
                    player.SendErrorMessage("/class add [name]");
                    player.SendErrorMessage("/class set [name] [category|desc|inv|stats]");
                    player.SendErrorMessage("/class del [name]");
                    player.SendErrorMessage("/class buff [add|del] [name] [buff] [duration]");
                    player.SendErrorMessage("/class itembuff [add|del] [name] [buff] [duration]");
                    player.SendErrorMessage("/class ammo [add|del] [name] [refresh time] [maximum ammo count]");
                }
                else
                {
                    player.SendErrorMessage("You do not have permission to manage classes");
                }
                return;
            }
            #endregion

            string param = args.Parameters[0].ToLower();
            args.Parameters.RemoveAt(0);

            if (param == "select")
            {
                if (args.Parameters.Count == 0)
                {
                    player.SendErrorMessage("Usage: /class select [name]");
                    return;
                }
                if (args.Parameters[0] == "none")
                {
                    resetClass(player);
                    return;
                }
                setclass(player, args.Parameters[0]);
                return;
            }

            if (param == "list")
            {
                List<Classvar> temp = new List<Classvar>();
                List<string> categories = new List<string>();
                int pagenum;
                foreach (Classvar c in classes)
                {
                    if (!categories.Contains(c.category))
                        categories.Add(c.category);
                }
                int a;
                if (args.Parameters.Count > 0 && int.TryParse(args.Parameters[0], out a))
                {
                    if (args.Parameters[0] == "category" || args.Parameters[0] == "categories")
                    {
                        if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pagenum))
                            return;
                        IEnumerable<string> catList = categories;
                        PaginationTools.SendPage(player, pagenum, PaginationTools.BuildLinesFromTerms(catList),
                            new PaginationTools.Settings
                            {
                                HeaderFormat = "Class Wars Class Categories:",
                                FooterFormat = "type /class list {{0}}",
                                NothingToDisplayString = "No categories are presently defined."
                            });
                        return;
                    }
                    foreach(Classvar c in classes)
                    {
                        if (c.category == args.Parameters[0])
                            temp.Add(c);
                    }
                    if (!PaginationTools.TryParsePageNumber(args.Parameters, 0, args.Player, out pagenum))
                        return;
                    IEnumerable<string> classList = from Classvar in temp
                                                    select Classvar.name;
                    PaginationTools.SendPage(player, pagenum, PaginationTools.BuildLinesFromTerms(classList),
                        new PaginationTools.Settings
                        {
                            HeaderFormat = "Classes in Category \"" + args.Parameters[0] + "\" :",
                            FooterFormat = "type /class list " + args.Parameters[0] + " {{0}}",
                            NothingToDisplayString = "No classes in this category are presently defined."
                        });
                    return;
                }
                if (!PaginationTools.TryParsePageNumber(args.Parameters, 0, args.Player, out pagenum))
                    return;
                IEnumerable<string> classesList = from Classvar in classes
                                                select Classvar.name;
                PaginationTools.SendPage(player, pagenum, PaginationTools.BuildLinesFromTerms(classesList),
                    new PaginationTools.Settings
                    {
                        HeaderFormat = "Class Wars Classes:",
                        FooterFormat = "type /class list {{0}}",
                        NothingToDisplayString = "No Class Wars classes are presently defined."
                    });
                return;
            }

            if (param == "preview")
            {
                if (args.Parameters.Count == 0)
                {
                    player.SendErrorMessage("Usage: /class preview [name]");
                    return;
                }
                previewclass(player, args.Parameters[0]);
                return;
            }

            if (param == "description" || param == "desc")
            {
                if (args.Parameters.Count == 0)
                {
                    player.SendErrorMessage("Usage: /class desc [name]");
                    return;
                }
                foreach(Classvar c in classes)
                {
                    if (c.name == args.Parameters[0])
                    {
                        player.SendInfoMessage(c.name + ":");
                        foreach (string d in c.description)
                            player.SendSuccessMessage(d);
                        return;
                    }
                }
                player.SendErrorMessage("Class " + args.Parameters[0] + " not found.");
                return;
            }

            if (!player.HasPermission("CS.admin"))
            {
                player.SendErrorMessage("Usage: /class select [name]");
                player.SendErrorMessage("/class list [category]");
                player.SendErrorMessage("/class preview [name]");
                player.SendErrorMessage("/class desc [name]");
                return;
            }

            if (param == "add")
            {
                if (args.Parameters.Count == 0)
                {
                    player.SendErrorMessage("Usage: /class add [name]");
                    return;
                }
                string name = args.Parameters[0];
                player.SaveServerCharacter();
                NetItem[] inv = player.PlayerData.inventory;
                int maxHP = player.PlayerData.maxHealth;
                int maxMana = player.PlayerData.maxMana;
                int? extraSlot = player.PlayerData.extraSlot;
                foreach(Classvar c in classes)
                {
                    if (c.name == name)
                    {
                        player.SendErrorMessage("Class " + c.name + " already exists");
                        return;
                    }
                }
                List<string> a = new List<string>();
                List<Buff> b = new List<Buff>();
                List<ItemBuff> ib = new List<ItemBuff>();
                List<Ammo> _ammo = new List<Ammo>();
                Classvar temp = new Classvar(name, "general", a, b, ib, _ammo, inv, maxHP, maxMana, extraSlot);
                class_db.AddClass(temp);
                classes.Clear();
                class_db.LoadClasses(ref classes);
                player.SendInfoMessage(temp.name + " successfully added.");
                return;
            }

            if (param == "set")
            {
                if (args.Parameters.Count < 2)
                {
                    player.SendErrorMessage("Usage: /class set [name] [category|desc|inv|stats]");
                    return;
                }
                string name = args.Parameters[0];
                string param2 = args.Parameters[1];

                if (param2 == "category" || param2 == "cat")
                {
                    if (args.Parameters.Count < 3)
                    {
                        player.SendErrorMessage("Usage: /class set [name] category [category]");
                        return;
                    }
                    string cat = args.Parameters[2];
                    foreach(Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            c.category = cat;
                            class_db.UpdateClass(c);
                            classes.Clear();
                            class_db.LoadClasses(ref classes);
                            player.SendSuccessMessage(c.name + " is now part of category \"" + c.category + "\"");
                            return;
                        }
                    }
                    player.SendErrorMessage("Class " + name + " not found.");
                    return;
                }

                if (param2 == "desc")
                {
                    if (args.Parameters.Count < 3)
                    {
                        player.SendErrorMessage("Usage: /class set [name] [desc] [add] [nextLine]");
                        player.SendErrorMessage("/class set [name] [desc] [del]");
                        return;
                    }
                    if (args.Parameters[2] == "add" && args.Parameters.Count < 4)
                    {
                        player.SendErrorMessage("Usage: /class set [name] [desc] [add] [nextLine]");
                        return;
                    }
                    if (args.Parameters[2] == "del")
                    {
                        foreach (Classvar c in classes.ToArray())
                        {
                            if (c.name == name)
                            {
                                player.SendSuccessMessage("Class " + c.name + " deleted.");
                                class_db.DeleteClass(c.name);
                                classes.Clear();
                                class_db.LoadClasses(ref classes);
                                return;
                            }
                        }
                        player.SendErrorMessage("Class " + name + " not found.");
                        return;
                    }
                    if (args.Parameters[2] == "add")
                    {
                        foreach (Classvar c in classes.ToArray())
                        {
                            if (c.name == name)
                            {
                                c.description.Add(args.Parameters[3]);
                                class_db.UpdateClass(c);
                                classes.Clear();
                                class_db.LoadClasses(ref classes);
                                player.SendInfoMessage("Current description for " + c.name + ".");
                                foreach (string x in c.description)
                                {
                                    player.SendSuccessMessage(x);
                                }
                                return;
                            }
                        }
                        player.SendErrorMessage("Class " + name + " not found.");
                        return;
                    }
                    player.SendErrorMessage("Usage: /class set [name] [desc] [add] [nextLine]");
                    player.SendErrorMessage("/class set [name] [desc] [del]");
                    return;
                }

                if (param2 == "inv")
                {
                    foreach(Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            player.SaveServerCharacter();
                            NetItem[] inv = player.PlayerData.inventory;
                            c.inventory = inv;
                            class_db.UpdateClass(c);
                            classes.Clear();
                            class_db.LoadClasses(ref classes);
                            player.SendSuccessMessage(c.name + "'s inventory has been udpated.");
                            return;
                        }
                    }
                    player.SendErrorMessage("Class " + name + " not found.");
                    return;
                }

                if (param2 == "stats" || param2 == "stat")
                {
                    foreach(Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            class_db.UpdateClass(c);
                            classes.Clear();
                            class_db.LoadClasses(ref classes);
                            player.SendSuccessMessage(c.name + " HP: " + c.maxHealth + ", Mana: " + c.maxMana);
                            return;
                        }
                    }
                }
            }

            if (param == "del")
            {
                if (args.Parameters.Count == 0)
                {
                    player.SendErrorMessage("Usage: /class del [name]");
                    return;
                }
                foreach(Classvar c in classes.ToArray())
                {
                    if (c.name == args.Parameters[0])
                    {
                        class_db.DeleteClass(c.name);
                        classes.Remove(c);
                        player.SendSuccessMessage(c.name + " deleted.");
                        return;
                    }
                }
                player.SendErrorMessage("Class " + args.Parameters[0] + " not found.");
                return;
            }

            if (param == "buff")
            {
                if (args.Parameters.Count < 3)
                {
                    player.SendErrorMessage("/class buff [add|del] [name] [buff] [duration]");
                    return;
                }

                string param2 = args.Parameters[0];
                args.Parameters.RemoveAt(0);
                string name = args.Parameters[0];
                args.Parameters.RemoveAt(0);

                List<int> buffs = TShock.Utils.GetBuffByName(args.Parameters[0]);
                if (buffs.Count == 0)
                {
                    player.SendErrorMessage("Buff not found.");
                    return;
                }
                else if (buffs.Count > 1)
                {
                    TShock.Utils.SendMultipleMatchError(player, buffs.Select(f => TShock.Utils.GetBuffName(f)));
                    return;
                }

                if (param2 == "add")
                {
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("/class buff add [name] [buff] [duration]");
                        return;
                    }
                    int duration;
                    if (!int.TryParse(args.Parameters[1], out duration))
                    {
                        player.SendErrorMessage("Unable to parse duration");
                        return;
                    }
                    foreach (Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            c.buffs.Add(new Buff(buffs[0], duration));
                            player.SendSuccessMessage(TShock.Utils.GetBuffName(buffs[0]) + " added to " + c.name + " with a " + duration + " second duration.");
                            class_db.UpdateClass(c);
                            classes.Clear();
                            class_db.LoadClasses(ref classes);
                            return;
                        }
                    }
                    player.SendErrorMessage("Class " + name + " not found.");
                    return;
                }

                if (param2 == "del")
                {
                    foreach(Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            int i = 0;
                            foreach (Buff b in c.buffs.ToArray())
                            {
                                if (b.id == buffs[0])
                                {
                                    player.SendSuccessMessage(TShock.Utils.GetBuffName(b.id) + " removed from " + c.name + ".");
                                    c.buffs.RemoveAt(i);
                                    class_db.UpdateClass(c);
                                    classes.Clear();
                                    class_db.LoadClasses(ref classes);
                                    return;
                                }
                                i++;
                            }
                            player.SendErrorMessage(c.name + " does not have buff " + TShock.Utils.GetBuffName(buffs[0]) + ".");
                            return;
                        }
                    }
                    player.SendErrorMessage("Class " + name + " not found.");
                    return;
                }
                
            }

            if (param == "itembuff")
            {
                if (args.Parameters.Count < 3)
                {
                    player.SendErrorMessage("/class itembuff [add|del] [name] [buff] [duration]");
                    return;
                }
                string param2 = args.Parameters[0];
                string name = args.Parameters[1];
                args.Parameters.RemoveAt(0);
                args.Parameters.RemoveAt(0);

                var buffs = TShock.Utils.GetBuffByName(args.Parameters[0]);
                if (buffs.Count == 0)
                {
                    player.SendErrorMessage("Buff not found.");
                    return;
                }
                else if (buffs.Count > 1)
                {
                    TShock.Utils.SendMultipleMatchError(player, buffs.Select(f => TShock.Utils.GetBuffName(f)));
                    return;
                }

                Item tempItem = player.TPlayer.HeldItem;

                if (param2 == "add")
                {
                    if (args.Parameters.Count < 2)
                    {
                        player.SendErrorMessage("/class itembuff [add|del] [name] [buff] [duration]");
                        return;
                    }
                    int duration;
                    if (!int.TryParse(args.Parameters[1], out duration))
                    {
                        player.SendErrorMessage("Unable to parse duration");
                        return;
                    }
                    foreach (Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            c.itembuffs.Add(new ItemBuff(buffs[0], duration, tempItem.netID));
                            player.SendSuccessMessage(c.name + " now gains " + TShock.Utils.GetBuffName(buffs[0]) + " while holding " + tempItem.Name + ".");
                            class_db.UpdateClass(c);
                            classes.Clear();
                            class_db.LoadClasses(ref classes);
                            return;
                        }
                    }
                    player.SendErrorMessage("Class " + name + " not found.");
                    return;
                }

                if (param2 == "del")
                {
                    foreach (Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            int _i = 0;
                            foreach(ItemBuff i in c.itembuffs.ToArray())
                            {
                                if (i.id == buffs[0])
                                {
                                    player.SendSuccessMessage(TShock.Utils.GetBuffName(i.id) + " removed from " + c.name + "'s " + tempItem.Name + ".");
                                    c.itembuffs.RemoveAt(_i);
                                    class_db.UpdateClass(c);
                                    classes.Clear();
                                    class_db.LoadClasses(ref classes);
                                    return;
                                }
                                _i++;
                            }
                            _i = 0;
                            player.SendErrorMessage(c.name + " does not contain itembuff " + TShock.Utils.GetBuffName(buffs[0]) + ".");
                            return;
                        }
                    }
                    player.SendErrorMessage("Class " + name + " not found.");
                    return;
                }
            }

            if (param == "ammo")
            {
                if (args.Parameters.Count < 2)
                {
                    player.SendErrorMessage("Usage: /class ammo [add|del] [name] [refresh time] [maximum ammo count]");
                    return;
                }
                string param2 = args.Parameters[0];
                string name = args.Parameters[1];
                args.Parameters.RemoveAt(0);
                args.Parameters.RemoveAt(0);
                Item tempItem = player.TPlayer.HeldItem;

                if (param2 == "add")
                {
                    if (args.Parameters.Count == 0)
                    {
                        player.SendErrorMessage("Usage: /class ammo add [name] [refresh time] [maximum ammo count]");
                    }
                    int refresh;
                    if (!int.TryParse(args.Parameters[0], out refresh))
                    {
                        player.SendErrorMessage("Unable to parse refresh time.");
                    }
                    if (refresh < 1)
                    {
                        player.SendErrorMessage("Refresh time must be at least 1 second");
                    }
                    int maxAmmo;
                    if (!int.TryParse(args.Parameters[1], out maxAmmo))
                    {
                        player.SendErrorMessage("Unable to parse maximum ammo count");
                    }
                    if (maxAmmo < 1)
                    {
                        player.SendErrorMessage("Maximum ammo count must be at least 1");
                    }
                    foreach(Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            c.ammo.Add(new Ammo(refresh, tempItem.netID, tempItem.stack, maxAmmo, tempItem.prefix));
                            class_db.UpdateClass(c);
                            if (tempItem.prefix == 0)
                                player.SendSuccessMessage(c.name + " will now recieve " + tempItem.stack + " " + tempItem.Name + " every " + refresh + " seconds.");
                            else
                                player.SendSuccessMessage(c.name + " will now recieve " + tempItem.stack + " " +  TShock.Utils.GetPrefixById(tempItem.prefix) + " " + tempItem.Name + " every " + refresh + " seconds.");
                            return;
                        }
                    }
                    player.SendErrorMessage("Class " + name + " not found.");
                    return;
                }

                if (param2 == "del")
                {
                    foreach(Classvar c in classes.ToArray())
                    {
                        if (c.name == name)
                        {
                            foreach (Ammo a in c.ammo.ToArray())
                            {
                                if (a.item == tempItem.netID)
                                {
                                    player.SendSuccessMessage(TShock.Utils.GetItemById(a.item).Name + " will no longer be refilled for " + c.name + ".");
                                    c.ammo.Remove(a);
                                    class_db.UpdateClass(c);
                                    classes.Clear();
                                    class_db.LoadClasses(ref classes);
                                    return;
                                }
                            }
                            player.SendErrorMessage(c.name + " does not refill " + tempItem.Name + ".");
                            return;
                        }
                    }
                    player.SendErrorMessage("Class " + name + " not found.");
                    return;
                }
            }
        }
        #endregion
    }
}