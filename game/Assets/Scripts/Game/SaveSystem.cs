using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    [Serializable]
    public class CampaignSave
    {
        public int SchemaVersion = 1;
        public string SavedAtUtc = "";
        public int MusterState, PartyGold;
        public List<int> ZoneStates = new List<int>();
        public List<int> ZoneClearedCounts = new List<int>();
        public bool CampaignComplete;
        public List<string> Stash = new List<string>();
        public List<string> ConsumedEncounters = new List<string>();
        /// <summary>Stable `zoneId|result` records for recoveries, rescues, controls,
        /// and player choices. Empty in older saves, which correctly means unresolved.</summary>
        public List<string> CompletedSiteActions = new List<string>();
        public List<SavedCharacter> Roster = new List<SavedCharacter>();
    }

    [Serializable]
    public class SavedCharacter
    {
        public string Name = "";
        public int ClassIndex, RaceIndex;
        public int Str, Dex, Con, Int, Wis, Cha;   // pre-racial base scores
        public int Xp, CurrentHp;
        public int[] SlotsRemaining = { 0, 0, 0 };

        /// <summary>Points spent per ability, in Ability order — NOT the final scores. The
        /// scores are derived (base + race + these), and derived state is never persisted:
        /// that is exactly the trap ZoneClearedCounts fell into. Saves written before
        /// levelling existed have no such field and JSON leaves it zeroed, which is the
        /// truth for them. Schema stays at 1 for that reason: an old save must still load.</summary>
        public int[] AbilityIncreases = { 0, 0, 0, 0, 0, 0 };
    }

    /// <summary>Host-owned campaign persistence (Phase 4): quest state, stash, cleared
    /// encounters, and the character roster keyed by display name so a rejoining player
    /// gets their own character back. File lives on the host machine only.</summary>
    public static class SaveSystem
    {
        /// <summary>`RadiantPool.exe -savedir &lt;path&gt;` puts the campaign somewhere else.
        /// The smoke test uses it so an automated run can never touch — or overwrite — the
        /// real campaign it would otherwise load and save straight over.</summary>
        private static string SaveDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-savedir" && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return args[i + 1];
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Saved Games", "RadiantPool");
        }

        public static string SavePath
        {
            get
            {
                string dir = SaveDir();
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "campaign.json");
            }
        }

        public static bool Exists => File.Exists(SavePath);

        public static void Write(CampaignSave save)
        {
            save.SavedAtUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(SavePath, JsonConvert.SerializeObject(save, Formatting.Indented));
            Debug.Log($"[Save] Campaign written to {SavePath}");
        }

        public static CampaignSave Read()
        {
            try
            {
                var save = JsonConvert.DeserializeObject<CampaignSave>(File.ReadAllText(SavePath));
                if (save == null || save.SchemaVersion != 1)
                {
                    Debug.LogWarning("[Save] Unreadable or newer-schema save; starting fresh.");
                    return null;
                }
                return save;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Failed to read save: {e.Message}; starting fresh.");
                return null;
            }
        }

        public static SavedCharacter Capture(CharacterSheet sheet, CharacterBuild build)
        {
            return new SavedCharacter
            {
                Name = sheet.Name,
                ClassIndex = build.ClassIndex, RaceIndex = build.RaceIndex,
                Str = build.Str, Dex = build.Dex, Con = build.Con,
                Int = build.Int, Wis = build.Wis, Cha = build.Cha,
                Xp = sheet.Xp, CurrentHp = sheet.CurrentHp,
                SlotsRemaining = sheet.SlotsRemaining.ToArray(),
                AbilityIncreases = sheet.AbilityIncreases.ToArray()
            };
        }

        /// <summary>Rebuilds a sheet from a saved character: recreate at level 1 from the
        /// stored base scores, replay XP/level-ups, then restore HP and spent slots.</summary>
        public static CharacterSheet Restore(SavedCharacter saved)
        {
            var build = new CharacterBuild
            {
                ClassIndex = saved.ClassIndex, RaceIndex = saved.RaceIndex,
                Str = saved.Str, Dex = saved.Dex, Con = saved.Con,
                Int = saved.Int, Wis = saved.Wis, Cha = saved.Cha
            };
            var sheet = PlayerCharacterHolder.CreateSheetFromBuild(saved.Name, build);
            sheet.GainXp(saved.Xp);
            while (sheet.CanLevelUp) sheet.LevelUp();

            // Replay where the points went. The level-ups above granted them; spending them
            // again reproduces both the raised scores AND what is still unspent — no need to
            // store either. Anything the rules now refuse (a save from a different house rule,
            // a score already at 20) is dropped rather than throwing the whole campaign away.
            for (int a = 0; a < 6 && a < saved.AbilityIncreases.Length; a++)
                for (int i = 0; i < saved.AbilityIncreases[a]; i++)
                {
                    if (!sheet.CanSpendPointOn((Ability)a))
                    {
                        Debug.LogWarning($"[Save] {saved.Name}: dropped a spent point in " +
                                         $"{(Ability)a} that the rules no longer allow.");
                        break;
                    }
                    sheet.SpendAbilityPoint((Ability)a);
                }

            sheet.RestoreAllSlots();
            for (int lvl = 1; lvl <= 3; lvl++)
            {
                int spend = sheet.SlotsRemaining[lvl - 1]
                            - Mathf.Clamp(saved.SlotsRemaining[lvl - 1], 0,
                                sheet.SlotsRemaining[lvl - 1]);
                for (int i = 0; i < spend; i++) sheet.ConsumeSlot(lvl);
            }
            int hp = Mathf.Clamp(saved.CurrentHp, 1, sheet.MaxHp);
            if (hp < sheet.CurrentHp)
                sheet.TakeDamage(sheet.CurrentHp - hp, DamageType.Bludgeoning);
            return sheet;
        }
    }
}
