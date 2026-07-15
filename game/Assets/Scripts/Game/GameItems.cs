using System.Collections.Generic;
using RadiantPool.Rules;

namespace RadiantPool.Game
{
    public enum ItemSlot { Weapon, Armor, Shield, Consumable, Ring }

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

        /// <summary>The mundane item a magical variant derives from ("" = this is the base).
        /// Class proficiency follows the base, so a Longsword +1 is usable by exactly whoever
        /// can swing a Longsword.</summary>
        public string BaseId = "";
        /// <summary>The "+N" on an enchanted weapon (adds to attack AND damage), armour, or
        /// shield (adds AC). Rings carry their bonuses in the fields below instead.</summary>
        public int MagicBonus;
        /// <summary>Cloth body armour for casters — a wizard has no armour proficiency, so a
        /// robe is the only thing it can wear. Drawn from the armour slot like any body piece.</summary>
        public bool CasterRobe;
        /// <summary>An off-hand item a caster can hold (a warding orb), where a shield is a
        /// martial's off-hand. Occupies the same slot; only one off-hand at a time.</summary>
        public bool CasterOffhand;
        // Rings (Slot == Ring): each is optional, so one ring can give AC while another gives
        // bite. Two ring slots exist; their bonuses sum with everything else.
        public int RingAc;
        public int RingSave;
        public int RingAttack;
        public int RingDamage;

        /// <summary>The mundane id whose class rules and identity this item follows.</summary>
        public string Base => string.IsNullOrEmpty(BaseId) ? Id : BaseId;

        /// <summary>Shield bonus is flat +2 AC in SRD 5.1 (a magical shield adds its plus).</summary>
        public const int ShieldAcBonus = 2;

        /// <summary>Off-hand AC this item grants: a shield's +2 (plus its enchantment) or a
        /// caster orb's plus. Zero for anything that is not an off-hand piece.</summary>
        public int OffhandAcBonus => Slot == ItemSlot.Shield
            ? (CasterOffhand ? 0 : ShieldAcBonus) + MagicBonus : 0;

        /// <summary>Display name including the enchantment, e.g. "Longsword +1".</summary>
        public string DisplayName => MagicBonus > 0 ? $"{Name} +{MagicBonus}" : Name;

        private static string Plus(int n) => n >= 0 ? $"+{n}" : n.ToString();

        /// <summary>What this item does, in the terms a player cares about: weapon damage
        /// and reach, armour protection and its Dex cap, shield bonus, ring boon, potion.</summary>
        public string StatLine() => Slot switch
        {
            ItemSlot.Weapon =>
                $"{Damage}{(MagicBonus != 0 ? Plus(MagicBonus) : "")} "
                + $"{DamageType.ToString().ToLowerInvariant()} · "
                + (RangeFeet > 5 ? $"ranged {RangeFeet} ft" : "melee")
                + (Finesse ? " · finesse" : "")
                + (MagicBonus != 0 ? $" · {Plus(MagicBonus)} to hit" : ""),
            ItemSlot.Armor when Armor != null =>
                $"AC {Armor.BaseAc + MagicBonus}"
                + (Armor.MaxDexBonus == 0 ? " · no Dex bonus"
                    : Armor.MaxDexBonus == int.MaxValue ? " + Dex"
                    : $" + Dex (max +{Armor.MaxDexBonus})")
                + $" · {(CasterRobe ? "cloth" : Armor.Kind.ToString().ToLowerInvariant())}",
            ItemSlot.Shield => $"+{OffhandAcBonus} AC · off hand"
                + (CasterOffhand ? " · caster" : ""),
            ItemSlot.Ring => string.Join(" · ", RingBoons()),
            _ => Id == "potion_healing" ? "restores 2d4+2 HP" : "",
        };

        private System.Collections.Generic.IEnumerable<string> RingBoons()
        {
            if (RingAc != 0) yield return $"{Plus(RingAc)} AC";
            if (RingSave != 0) yield return $"{Plus(RingSave)} saves";
            if (RingAttack != 0) yield return $"{Plus(RingAttack)} to hit";
            if (RingDamage != 0) yield return $"{Plus(RingDamage)} damage";
        }

        /// <summary>The AC this armour/shield would give with the character's Dex, magic in.</summary>
        public int AcWith(int dexMod, bool withShield) => Slot switch
        {
            ItemSlot.Armor when Armor != null =>
                Armor.BaseAc + MagicBonus + System.Math.Min(dexMod, Armor.MaxDexBonus)
                + (withShield ? ShieldAcBonus : 0),
            _ => 0
        };

        /// <summary>Average damage per hit including the ability modifier the character
        /// would actually add (finesse/ranged take the better of Str and Dex) and any
        /// weapon enchantment.</summary>
        public float AverageDamage(int strMod, int dexMod)
        {
            if (Slot != ItemSlot.Weapon || string.IsNullOrEmpty(Damage)) return 0f;
            int mod = Finesse || RangeFeet > 5 ? System.Math.Max(strMod, dexMod) : strMod;
            return DiceExpression.Parse(Damage).Average + mod + MagicBonus;
        }

        public bool UsableBy(CharacterClass c) => Slot switch
        {
            // SRD proficiencies keyed off the mundane base, so a Longsword +1 follows the
            // Longsword: the wizard's two, the rogue's finesse-and-bows, and the cleric's
            // simple/blunt list. A fighter uses anything.
            ItemSlot.Weapon => c switch
            {
                CharacterClass.Wizard => Base is "dagger" or "quarterstaff" or "runed_staff",
                CharacterClass.Rogue => Finesse || Base is "shortbow" or "light_crossbow",
                CharacterClass.Cleric => Base is "mace" or "warhammer" or "quarterstaff" or "runed_staff"
                                          or "light_crossbow" or "dagger",
                _ => true
            },
            // A robe is cloth: only the wizard, who wears nothing else, equips it. Metal armour
            // stays off the wizard and off the rogue above light.
            ItemSlot.Armor => c switch
            {
                CharacterClass.Wizard => CasterRobe,
                CharacterClass.Rogue => !CasterRobe && Armor?.Kind == ArmorKind.Light,
                _ => !CasterRobe
            },
            // A shield is the martial off hand; a caster orb is the wizard/cleric one.
            ItemSlot.Shield => CasterOffhand
                ? c is CharacterClass.Wizard or CharacterClass.Cleric
                : c is CharacterClass.Fighter or CharacterClass.Cleric,
            // Rings fit any hand.
            ItemSlot.Ring => true,
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
                // Its d8 is the SRD versatile quarterstaff die, represented as a
                // dedicated two-handed adventuring staff for caster progression.
                new GameItem { Id = "runed_staff", Name = "Runed Staff", Slot = ItemSlot.Weapon,
                    Damage = "1d8", DamageType = DamageType.Bludgeoning,
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

                // ---- Caster cloth: the wizard's body slot, scaling like plate does the fighter.
                new GameItem { Id = "apprentice_robe", Name = "Apprentice Robe",
                    Slot = ItemSlot.Armor, Armor = ArmorDefinition.ApprenticeRobe, CasterRobe = true },
                new GameItem { Id = "warded_robe", Name = "Warded Robe",
                    Slot = ItemSlot.Armor, Armor = ArmorDefinition.WardedRobe, CasterRobe = true },
                new GameItem { Id = "archmage_robe", Name = "Archmage Robe",
                    Slot = ItemSlot.Armor, Armor = ArmorDefinition.ArchmageRobe, CasterRobe = true },

                // ---- Off-hand pieces beyond the plain shield.
                new GameItem { Id = "shield_1", Name = "Shield", BaseId = "shield",
                    Slot = ItemSlot.Shield, MagicBonus = 1 },
                new GameItem { Id = "shield_2", Name = "Shield", BaseId = "shield",
                    Slot = ItemSlot.Shield, MagicBonus = 2 },
                new GameItem { Id = "warding_orb", Name = "Warding Orb",
                    Slot = ItemSlot.Shield, MagicBonus = 1, CasterOffhand = true },

                // ---- Rings (two ring slots; boons stack with everything).
                new GameItem { Id = "ring_warding", Name = "Ring of Warding",
                    Slot = ItemSlot.Ring, RingSave = 1 },
                new GameItem { Id = "ring_precision", Name = "Ring of Precision",
                    Slot = ItemSlot.Ring, RingAttack = 1 },
                new GameItem { Id = "ring_protection_1", Name = "Ring of Protection",
                    Slot = ItemSlot.Ring, RingAc = 1, RingSave = 1 },
                new GameItem { Id = "ring_warrior", Name = "Ring of the Warrior",
                    Slot = ItemSlot.Ring, RingAttack = 1, RingDamage = 1 },
                new GameItem { Id = "ring_protection_2", Name = "Greater Ring of Protection",
                    Slot = ItemSlot.Ring, RingAc = 2, RingSave = 2 },
            };
            var dict = new Dictionary<string, GameItem>();
            foreach (var i in list) dict[i.Id] = i;

            // Enchanted variants are DERIVED from the mundane bases already in dict, so a
            // "+N" can never drift from the item it is a better copy of. Class proficiency and
            // the hand model come along through BaseId. These fill the higher loot tiers, so
            // the plus you carry tracks the level you have reached.
            void MagicWeapon(string baseId, int plus)
            {
                var b = dict[baseId];
                dict[$"{baseId}_{plus}"] = new GameItem
                {
                    Id = $"{baseId}_{plus}", Name = b.Name, BaseId = baseId,
                    Slot = ItemSlot.Weapon, Damage = b.Damage, DamageType = b.DamageType,
                    Finesse = b.Finesse, RangeFeet = b.RangeFeet, HandModel = b.HandModel,
                    MagicBonus = plus,
                };
            }
            void MagicArmor(string baseId, int plus)
            {
                var b = dict[baseId];
                dict[$"{baseId}_{plus}"] = new GameItem
                {
                    Id = $"{baseId}_{plus}", Name = b.Name, BaseId = baseId,
                    Slot = ItemSlot.Armor, Armor = b.Armor, MagicBonus = plus,
                };
            }

            MagicWeapon("dagger", 1);        // wizard/rogue, early
            MagicWeapon("mace", 1);          // cleric
            MagicWeapon("warhammer", 1);     // cleric
            MagicWeapon("rapier", 1);        // rogue
            MagicWeapon("longsword", 1);
            MagicWeapon("runed_staff", 1);   // wizard
            MagicWeapon("runed_staff", 2);
            MagicWeapon("longbow", 1);
            MagicWeapon("greatsword", 1);
            MagicWeapon("greatsword", 2);
            MagicWeapon("greataxe", 2);

            MagicArmor("studded_leather", 1);
            MagicArmor("chain_mail", 1);
            MagicArmor("half_plate", 1);
            MagicArmor("splint", 1);
            MagicArmor("splint", 2);

            return dict;
        }
    }

    /// <summary>Maps the recruiting player's equipped gear onto each hire. Exact items are
    /// copied whenever the hire's class can use them; otherwise the hire gets the closest
    /// class-legal item from the same progression tier. That keeps the party equally geared
    /// without putting a wizard in splint or handing a rogue a greataxe.</summary>
    public static class CompanionLoadout
    {
        public static string WeaponFor(CharacterClass hireClass, string leaderWeaponId)
        {
            var source = GameItem.Get(leaderWeaponId);
            if (source != null && source.Slot == ItemSlot.Weapon && source.UsableBy(hireClass))
                return source.Id;

            int tier = WeaponTier(leaderWeaponId);
            return hireClass switch
            {
                CharacterClass.Fighter => tier >= 3 ? "greatsword"
                    : tier >= 2 ? "warhammer" : "longsword",
                CharacterClass.Cleric => tier >= 2 ? "warhammer" : "mace",
                CharacterClass.Rogue => tier >= 2 ? "rapier"
                    : source != null && source.RangeFeet > 5 ? "shortbow" : "shortsword",
                CharacterClass.Wizard => tier >= 2 ? "runed_staff" : "quarterstaff",
                _ => "dagger"
            };
        }

        public static string ArmorFor(CharacterClass hireClass, string leaderArmorId)
        {
            var source = GameItem.Get(leaderArmorId);
            if (source == null || source.Slot != ItemSlot.Armor)
                return "";
            if (source.UsableBy(hireClass)) return source.Id;

            int tier = ArmorTier(leaderArmorId);
            return hireClass switch
            {
                CharacterClass.Rogue => tier >= 2 ? "studded_leather" : "leather_armor",
                CharacterClass.Wizard => "",
                _ => tier >= 4 ? "splint" : tier >= 3 ? "half_plate"
                    : tier >= 2 ? "scale_mail" : "leather_armor"
            };
        }

        public static bool ShieldFor(CharacterClass hireClass, bool leaderHasShield) =>
            leaderHasShield && GameItem.Get("shield").UsableBy(hireClass);

        public static int WeaponTier(string id) => id switch
        {
            "rapier" or "warhammer" or "runed_staff" => 2,
            "greatsword" or "greataxe" or "longbow" => 3,
            _ => 1
        };

        public static int ArmorTier(string id) => id switch
        {
            "studded_leather" => 2,
            "half_plate" => 3,
            "splint" => 4,
            "leather_armor" or "scale_mail" or "chain_mail" => 1,
            _ => 0
        };
    }
}
