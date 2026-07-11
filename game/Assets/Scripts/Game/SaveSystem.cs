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
        public int MusterState, ClearQuestState, EncountersCleared, PartyGold;
        public bool ZonePacified;
        public List<string> Stash = new List<string>();
        public List<string> ConsumedEncounters = new List<string>();
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
    }

    /// <summary>Host-owned campaign persistence (Phase 4): quest state, stash, cleared
    /// encounters, and the character roster keyed by display name so a rejoining player
    /// gets their own character back. File lives on the host machine only.</summary>
    public static class SaveSystem
    {
        public static string SavePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Saved Games", "RadiantPool");
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
                SlotsRemaining = sheet.SlotsRemaining.ToArray()
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
