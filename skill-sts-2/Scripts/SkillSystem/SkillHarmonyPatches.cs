using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Logging;

namespace skill_sts2.Scripts.SkillSystem;

[HarmonyPatch]
internal static class SkillHarmonyPatches
{
    [HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
    [HarmonyPostfix]
    private static void TopBarInitializePostfix(NTopBar __instance, IRunState runState)
    {
        try
        {
            CharacterSkillSystem.OnTopBarInitialized(__instance, runState);
        }
        catch (Exception ex)
        {
            Log.Error($"[SkillMod] TopBarInitializePostfix failed: {ex}");
        }
    }

    [HarmonyPatch(typeof(NTopBar), nameof(NTopBar._ExitTree))]
    [HarmonyPostfix]
    private static void TopBarExitPostfix(NTopBar __instance)
    {
        try
        {
            CharacterSkillSystem.OnTopBarExited(__instance);
        }
        catch (Exception ex)
        {
            Log.Error($"[SkillMod] TopBarExitPostfix failed: {ex}");
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSinglePlayer))]
    [HarmonyPostfix]
    private static void SetUpNewSinglePlayerPostfix(RunState state)
    {
        CharacterSkillSystem.OnNewRunInitialized(state, isMultiplayer: false);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiPlayer))]
    [HarmonyPostfix]
    private static void SetUpNewMultiPlayerPostfix(RunState state)
    {
        CharacterSkillSystem.OnNewRunInitialized(state, isMultiplayer: true);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSinglePlayer))]
    [HarmonyPostfix]
    private static void SetUpSavedSinglePlayerPostfix(RunState state, SerializableRun save)
    {
        CharacterSkillSystem.OnRunLoaded(state, save, isMultiplayer: false);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiPlayer))]
    [HarmonyPostfix]
    private static void SetUpSavedMultiPlayerPostfix(RunState state, LoadRunLobby lobby)
    {
        CharacterSkillSystem.OnRunLoaded(state, lobby.Run, isMultiplayer: true);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.ToSave))]
    [HarmonyPostfix]
    private static void RunToSavePostfix(SerializableRun __result)
    {
        bool isMultiplayer = RunManager.Instance.NetService.Type != NetGameType.Singleplayer;
        CharacterSkillSystem.OnRunSerialized(__result, isMultiplayer);
    }

    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
        new[]
        {
            typeof(PlayerChoiceContext),
            typeof(IEnumerable<Creature>),
            typeof(decimal),
            typeof(ValueProp),
            typeof(Creature),
            typeof(CardModel)
        })]
    [HarmonyPostfix]
    private static void DamagePostfix(Task<IEnumerable<DamageResult>> __result, Creature? dealer)
    {
        CharacterSkillSystem.HandleDamageTask(__result, dealer);
    }
}
