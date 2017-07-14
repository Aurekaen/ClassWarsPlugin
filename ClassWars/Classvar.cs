using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using TShockAPI;

namespace ClassWars
{
    public class Classvar
    {
        public string name, category;
        public NetItem[] inventory;
        public List<string> description;
        public List<Buff> buffs;
        public List<ItemBuff> itembuffs;
        public List<Ammo> ammo;
        public int maxHealth, maxMana;
        public int? extraSlot;

        public Classvar(string name, string category, List<string> description, List<Buff> buffs, List<ItemBuff> itembuffs, List<Ammo> ammo, NetItem[] inventory, int maxHealth, int maxMana, int? extraSlot)
        {
            this.name = name;
            this.category = category;
            this.description = description;
            this.buffs = buffs;
            this.itembuffs = itembuffs;
            this.ammo = ammo;
            this.inventory = inventory;
            this.maxHealth = maxHealth;
            this.maxMana = maxMana;
            this.extraSlot = extraSlot;
        }
    }

    public class StorageFriendlyClassvar
    {
        public string name, category, description, buffs, itembuffs, ammo, inventory;
        public int maxHealth, maxMana;
        public int? extraSlot;

        public StorageFriendlyClassvar(string name, string category, string description, string buffs, string itembuffs, string ammo, string inventory, int maxHealth, int maxMana, int? extraSlot)
        {
            this.name = name;
            this.category = category;
            this.description = description;
            this.buffs = buffs;
            this.itembuffs = itembuffs;
            this.ammo = ammo;
            this.inventory = inventory;
            this.maxHealth = maxHealth;
            this.maxMana = maxMana;
            this.extraSlot = extraSlot;
        }
    }

    public class Buff
    {
        public int id, duration;

        public Buff(int id, int duration)
        {
            this.id = id;
            this.duration = duration;
        }
    }

    public class ItemBuff
    {
        public int id, duration, item;

        public ItemBuff(int id, int duration, int item)
        {
            this.id = id;
            this.duration = duration;
            this.item = item;
        }
    }

    public class Ammo
    {
        public int refresh, item, quantity;

        public Ammo(int refresh, int item, int quantity)
        {
            this.refresh = refresh;
            this.item = item;
            this.quantity = quantity;
        }
    }
}   