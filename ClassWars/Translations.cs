using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI;

namespace ClassWars
{
    class Translations
    {

        public static StorageFriendlyClassvar dataPrep(Classvar classvar)
        {
            StorageFriendlyClassvar x = new StorageFriendlyClassvar(classvar.name, classvar.category, string.Join("☺", classvar.description), buffBlob(classvar.buffs), ItemBuffBlob(classvar.itembuffs), AmmoBlob(classvar.ammo), String.Join("☺", classvar.inventory), classvar.maxHealth, classvar.maxMana, classvar.extraSlot);
            return x;
        }

        public static Classvar dataUnPrep(StorageFriendlyClassvar x)
        {
            string[] y = x.description.Split('☺');
            List<string> z = new List<string>(y);
            List<NetItem> a = x.inventory.Split('☺').Select(NetItem.Parse).ToList();
            if (a.Count < NetItem.MaxInventory)
            {
                //Set new armour slots empty
                a.InsertRange(67, new NetItem[2]);
                //Set new vanity slots empty
                a.InsertRange(77, new NetItem[2]);
                //Set new dye slots empty
                a.InsertRange(87, new NetItem[2]);
                //Set the rest of the new slots empty
                a.AddRange(new NetItem[NetItem.MaxInventory - a.Count]);
            }
            Classvar temp = new Classvar(x.name, x.category, z, buffUnblob(x.buffs), ItemBuffUnblob(x.itembuffs), AmmoUnblob(x.ammo), a.ToArray(), x.maxHealth, x.maxMana, x.extraSlot);
            return temp;
        }

        public static string buffBlob(List<Buff> x)
        {
            List<string> a = new List<string>();
            foreach (Buff b in x)
            {
                a.Add(b.id + "|" + b.duration);
            }
            string temp = string.Join("☺", a);
            return temp;
        }

        public static List<Buff> buffUnblob(string x)
        {
            List<Buff> temp = new List<Buff>();
            string[] a = x.Split('☺');
            Buff b = new Buff(0, 0);
            string[] c;
            int d, e;
            foreach (string s in a)
            {
                c = s.Split('|');
                if (int.TryParse(c[0], out d) && int.TryParse(c[1], out e))
                {
                    b.id = d;
                    b.duration = e;
                    temp.Add(b);
                }
            }
            return temp;
        }
        public static string ItemBuffBlob(List<ItemBuff> x)
        {
            List<string> a = new List<string>();
            foreach (ItemBuff b in x)
            {
                a.Add(b.id + "|" + b.duration + "|" + b.item);
            }
            string temp = string.Join("☺", a);
            return temp;
        }

        public static List<ItemBuff> ItemBuffUnblob(string x)
        {
            List<ItemBuff> temp = new List<ItemBuff>();
            string[] a = x.Split('☺');
            ItemBuff b = new ItemBuff(0, 0, 0);
            string[] c;
            int d, e, f;
            foreach (string s in a)
            {
                c = s.Split('|');
                if (int.TryParse(c[0], out d) && int.TryParse(c[1], out e) && int.TryParse(c[2], out f))
                {
                    b.id = d;
                    b.duration = e;
                    b.item = f;
                    temp.Add(b);
                }
            }
            return temp;
        }

        public static string AmmoBlob(List<Ammo> x)
        {
            List<string> a = new List<string>();
            foreach (Ammo b in x)
            {
                a.Add(b.refresh + "|" + b.item + "|" + b.quantity + "|" + b.maxCount + "|" + b.prefix);
            }
            string temp = string.Join("☺", a);
            return temp;
        }

        public static List<Ammo> AmmoUnblob(string x)
        {
            List<Ammo> temp = new List<Ammo>();
            string[] a = x.Split('☺');
            Ammo b = new Ammo(0, 0, 0, 0, 0);
            string[] c;
            int d, e, f, g, h;
            foreach (string s in a)
            {
                c = s.Split('|');
                if (int.TryParse(c[0], out d) && int.TryParse(c[1], out e) && int.TryParse(c[2], out f) && int.TryParse(c[2], out g) && int.TryParse(c[2], out h))
                {
                    b.refresh = d;
                    b.item = e;
                    b.quantity = f;
                    b.maxCount = g;
                    b.prefix = h;
                    temp.Add(b);
                }
            }
            return temp;
        }
    }
}