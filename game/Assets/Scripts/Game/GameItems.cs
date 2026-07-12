using System.Collections.Generic;
using RadiantPool.Rules;

namespace RadiantPool.Game
{
    public enum ItemSlot { Weapon, Armor, Shield, Consumable }

    /// <summary>Runtime item table mirroring content/items/items.json: stats for
    /// equipping, class restrictions, and which KayKit model to put in the hand.</summary>
    public sealed class GameItem
    {
        public string Id = "";
        public string Name = "";
        public ItemSlot Slot;
        // Weapons
        public string Damage = "";
        public DamageType DamageType;
        public bool Finesse;
        public int RangeFeet = 5;
        public string HandModel = "";      // Resources/Weapons/<model>
        // Armor
        public ArmorDefinition Armor;

        public bool UsableBy(CharacterClass c) => Slot switch
        {
            ItemSlot.Weapon => c switch
            {
                CharacterClass.Wizard => Id is "dagger" or "quarterstaff",
                CharacterClass.Rogue => Finesse || Id is "shortbow" or "light_crossbow",
                CharacterClass.Cleric => Id is "mace" or "quarterstaff" or "light_crossbow"
                                          or "dagger",
                _ => true
            },
            ItemSlot.Armor => c switch
            {
                CharacterClass.Wizard => false,
                CharacterClass.Rogue => Armor?.Kind == ArmorKind.Light,
                _ => true
            },
            ItemSlot.Shield => c is CharacterClass.Fighter or CharacterClass.Cleric,
            _ => true
        };

        public static readonly Dictionary<string, GameItem> All = Build();

        public static GameItem Get(string id) => All.TryGetValue(id, out var i) ? i : null;

        private static Dictionary<string, GameItem> Build()
        {
            var list = new List<GameItem>
            {
                new GameItem { Id = "dagger", Name = "Dagger", Slot = ItemSlot.Weapon,
                    Damage = "1d4", DamageType = DamageType.Piercing, Finesse = true,
                    HandModel = "dagger" },
                new GameItem { Id = "shortsword", Name = "Shortsword", Slot = ItemSlot.Weapon,
                    Damage = "1d6", DamageType = DamageType.Piercing, Finesse = true,
                    HandModel = "Skeleton_Blade" },
                new GameItem { Id = "longsword", Name = "Longsword", Slot = ItemSlot.Weapon,
                    Damage = "1d8", DamageType = DamageType.Slashing,
                    HandModel = "sword_1handed" },
                new GameItem { Id = "mace", Name = "Mace", Slot = ItemSlot.Weapon,
                    Damage = "1d6", DamageType = DamageType.Bludgeoning,
                    HandModel = "axe_1handed" },
                new GameItem { Id = "quarterstaff", Name = "Quarterstaff", Slot = ItemSlot.Weapon,
                    Damage = "1d6", DamageType = DamageType.Bludgeoning,
                    HandModel = "staff" },
                new GameItem { Id = "light_crossbow", Name = "Light Crossbow", Slot = ItemSlot.Weapon,
                    Damage = "1d8", DamageType = DamageType.Piercing, RangeFeet = 80,
                    HandModel = "crossbow_1handed" },
                new GameItem { Id = "shortbow", Name = "Shortbow", Slot = ItemSlot.Weapon,
                    Damage = "1d6", DamageType = DamageType.Piercing, RangeFeet = 80,
                    HandModel = "bow" },
                new GameItem { Id = "leather_armor", Name = "Leather Armor", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.Leather },
                new GameItem { Id = "scale_mail", Name = "Scale Mail", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.ScaleMail },
                new GameItem { Id = "chain_mail", Name = "Chain Mail", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.ChainMail },
                new GameItem { Id = "shield", Name = "Shield", Slot = ItemSlot.Shield },
                new GameItem { Id = "potion_healing", Name = "Potion of Healing",
                    Slot = ItemSlot.Consumable },
                new GameItem { Id = "torch", Name = "Torch", Slot = ItemSlot.Consumable },
            };
            var dict = new Dictionary<string, GameItem>();
            foreach (var i in list) dict[i.Id] = i;
            return dict;
        }
    }
}
