namespace ClassWars
{
    public class Arena
    {
        public int id;
        public string name;
        public Vector2 host, rSpawn, bSpawn, arenaTopL, arenaBottomR, switchPos;
//Hey, this is an edit! Can you see this edit? It is an edited line that had been edited from it's original edit.
//A Wild Edit Appeared!
        public Arena(string name, Vector2 host, Vector2 rSpawn, Vector2 bSpawn, Vector2 arenaTopL, Vector2 arenaBottomR, Vector2 switchPos)
        {
            this.name = name;
            this.host = host;
            this.rSpawn = rSpawn;
            this.bSpawn = bSpawn;
            this.arenaTopL = arenaTopL;
            this.arenaBottomR = arenaBottomR;
            this.switchPos = switchPos;
        }
    }
}
