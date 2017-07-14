using TShockAPI;
using System.Collections.Generic;

namespace ClassWars
{
    public static class InfoExtensions
    {
        public static PlayerInfo GetPlayerInfo(this TSPlayer player)
        {
            if (!player.ContainsData(PlayerInfo.KEY))
                player.SetData(PlayerInfo.KEY, new PlayerInfo());
            return player.GetData<PlayerInfo>(PlayerInfo.KEY);
        }
    }

    public static class ClassInfo
    {
        public static Classvar ClassLookup(string name, List<Classvar> classes)
        {
            foreach (Classvar c in classes)
            {
                if (c.name == name)
                {
                    return c;
                }
            }
            return null;
        }
    }
}
