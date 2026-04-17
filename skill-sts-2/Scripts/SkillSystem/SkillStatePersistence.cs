using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Saves;
using FileAccess = Godot.FileAccess;

namespace skill_sts2.Scripts.SkillSystem;

internal static class SkillStatePersistence
{
    private const string SingleRunStateFileName = "current_run.skill_sts2_state.json";
    private const string MultiplayerRunStateFileName = "current_run_mp.skill_sts2_state.json";

    public static Dictionary<ulong, PlayerSkillRuntimeSnapshot>? LoadSnapshots(SerializableRun save, bool isMultiplayer)
    {
        try
        {
            string path = GetStateFilePath(isMultiplayer);
            if (!FileAccess.FileExists(path))
            {
                return null;
            }

            FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                Log.Warn($"[SkillMod] Failed to open state file for read: {path}");
                return null;
            }

            string json = file.GetAsText();
            file.Close();
            SkillRuntimeSaveFile? payload = JsonSerializer.Deserialize<SkillRuntimeSaveFile>(json);
            if (payload == null)
            {
                return null;
            }

            if (!string.Equals(payload.RunKey, BuildRunKey(save), StringComparison.Ordinal))
            {
                return null;
            }

            return payload.Players;
        }
        catch (Exception ex)
        {
            Log.Warn($"[SkillMod] Failed to load runtime state sidecar: {ex.Message}");
            return null;
        }
    }

    public static void SaveSnapshots(SerializableRun save, bool isMultiplayer, IReadOnlyDictionary<ulong, PlayerSkillRuntimeSnapshot> snapshots)
    {
        try
        {
            string path = GetStateFilePath(isMultiplayer);
            SkillRuntimeSaveFile payload = new()
            {
                RunKey = BuildRunKey(save),
                Players = snapshots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            string json = JsonSerializer.Serialize(payload);
            FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                Log.Warn($"[SkillMod] Failed to open state file for write: {path}");
                return;
            }

            file.StoreString(json);
            file.Close();
        }
        catch (Exception ex)
        {
            Log.Warn($"[SkillMod] Failed to save runtime state sidecar: {ex.Message}");
        }
    }

    private static string GetStateFilePath(bool isMultiplayer)
    {
        int profileId = SaveManager.Instance.CurrentProfileId;
        string saveDir = UserDataPathProvider.GetProfileScopedPath(profileId, UserDataPathProvider.SavesDir);
        string fileName = isMultiplayer ? MultiplayerRunStateFileName : SingleRunStateFileName;
        return $"{saveDir}/{fileName}";
    }

    private static string BuildRunKey(SerializableRun save)
    {
        string playerIds = string.Join(",", save.Players.Select(player => player.NetId).OrderBy(id => id));
        return $"{save.StartTime}|{save.SerializableRng.Seed}|{playerIds}";
    }

    private sealed class SkillRuntimeSaveFile
    {
        public string RunKey { get; set; } = string.Empty;

        public Dictionary<ulong, PlayerSkillRuntimeSnapshot> Players { get; set; } = new();
    }
}
