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
        public static List<Classvar> ClassLookup(string name, List<Classvar> classes)
        {
            List<Classvar> x = new List<Classvar>();
            bool found = false;
            foreach (Classvar c in classes)
            {
                if (c.name.StartsWith(name) || c.name == name)
                {
                    found = true;
                    x.Add(c);
                }
            }
            if (found)
                return x;
            return null;
        }
    }
}
