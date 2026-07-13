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

        /// <summary>Shield bonus is flat +2 AC in SRD 5.1.</summary>
        public const int ShieldAcBonus = 2;

        /// <summary>What this item does, in the terms a player cares about: weapon damage
        /// and reach, armour protection and its Dex cap, shield bonus, potion healing.</summary>
        public string StatLine() => Slot switch
        {
            ItemSlot.Weapon =>
                $"{Damage} {DamageType.ToString().ToLowerInvariant()} · "
                + (RangeFeet > 5 ? $"ranged {RangeFeet} ft" : "melee")
                + (Finesse ? " · finesse" : ""),
            ItemSlot.Armor when Armor != null =>
                $"AC {Armor.BaseAc}"
                + (Armor.MaxDexBonus == 0 ? " · no Dex bonus"
                    : Armor.MaxDexBonus == int.MaxValue ? " + Dex"
                    : $" + Dex (max +{Armor.MaxDexBonus})")
                + $" · {Armor.Kind.ToString().ToLowerInvariant()}",
            ItemSlot.Shield => $"+{ShieldAcBonus} AC · off hand",
            _ => Id == "potion_healing" ? "restores 2d4+2 HP" : "",
        };

        /// <summary>The AC this armour/shield would give with the character's Dex.</summary>
        public int AcWith(int dexMod, bool withShield) => Slot switch
        {
            ItemSlot.Armor when Armor != null =>
                Armor.BaseAc + System.Math.Min(dexMod, Armor.MaxDexBonus)
                + (withShield ? ShieldAcBonus : 0),
            _ => 0
        };

        /// <summary>Average damage per hit including the ability modifier the character
        /// would actually add (finesse/ranged take the better of Str and Dex).</summary>
        public float AverageDamage(int strMod, int dexMod)
        {
            if (Slot != ItemSlot.Weapon || string.IsNullOrEmpty(Damage)) return 0f;
            int mod = Finesse || RangeFeet > 5 ? System.Math.Max(strMod, dexMod) : strMod;
            return DiceExpression.Parse(Damage).Average + mod;
        }

        public bool UsableBy(CharacterClass c) => Slot switch
        {
            // SRD proficiencies: the wizard's two, the rogue's finesse-and-bows, and the
            // cleric's simple/blunt list — a warhammer is a cleric's upgrade over the mace,
            // a rapier is the rogue's over the shortsword. A fighter uses anything.
            ItemSlot.Weapon => c switch
            {
                CharacterClass.Wizard => Id is "dagger" or "quarterstaff",
                CharacterClass.Rogue => Finesse || Id is "shortbow" or "light_crossbow",
                CharacterClass.Cleric => Id is "mace" or "warhammer" or "quarterstaff"
                                          or "light_crossbow" or "dagger",
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
                // The gear the deeper zones pay out (see LootLibrary): a rogue's rapier, a
                // cleric's hammer, the two-handers, and the armour that beats chain.
                new GameItem { Id = "rapier", Name = "Rapier", Slot = ItemSlot.Weapon,
                    Damage = "1d8", DamageType = DamageType.Piercing, Finesse = true,
                    HandModel = "sword_1handed" },
                new GameItem { Id = "warhammer", Name = "Warhammer", Slot = ItemSlot.Weapon,
                    Damage = "1d8", DamageType = DamageType.Bludgeoning,
                    HandModel = "axe_1handed" },
                new GameItem { Id = "greatsword", Name = "Greatsword", Slot = ItemSlot.Weapon,
                    Damage = "2d6", DamageType = DamageType.Slashing,
                    HandModel = "sword_2handed" },
                new GameItem { Id = "greataxe", Name = "Greataxe", Slot = ItemSlot.Weapon,
                    Damage = "1d12", DamageType = DamageType.Slashing,
                    HandModel = "axe_2handed" },
                new GameItem { Id = "longbow", Name = "Longbow", Slot = ItemSlot.Weapon,
                    Damage = "1d8", DamageType = DamageType.Piercing, RangeFeet = 150,
                    HandModel = "bow" },
                new GameItem { Id = "leather_armor", Name = "Leather Armor", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.Leather },
                new GameItem { Id = "studded_leather", Name = "Studded Leather", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.StuddedLeather },
                new GameItem { Id = "scale_mail", Name = "Scale Mail", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.ScaleMail },
                new GameItem { Id = "half_plate", Name = "Half Plate", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.HalfPlate },
                new GameItem { Id = "chain_mail", Name = "Chain Mail", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.ChainMail },
                new GameItem { Id = "splint", Name = "Splint", Slot = ItemSlot.Armor,
                    Armor = ArmorDefinition.Splint },
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
