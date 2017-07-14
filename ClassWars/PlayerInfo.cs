using TShockAPI;

namespace ClassWars
{
    public class PlayerInfo
    {

        public const string KEY = "classwars_data";
        public PlayerData backup { get; set; }
        public Classvar classInfo { get; set; }
        public bool preview { get; set; }

        public PlayerInfo()
        {
            backup = null;
            classInfo = null;
            preview = false;
        }

        public bool Restore(TSPlayer player)
        {
            if (backup == null)
                return false;
            backup.RestoreCharacter(player);
            backup = null;
            return true;
        }
    }
}
