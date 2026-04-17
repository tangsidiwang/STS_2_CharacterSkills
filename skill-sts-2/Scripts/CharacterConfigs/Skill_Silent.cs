using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.ValueProps;
using skill_sts2.Scripts.SkillSystem;

namespace skill_sts2.Scripts.CharacterConfigs;

public sealed class SilentSkillProvider : ICharacterSkillProvider
{
    public CharacterSkillProfile Build()
    {
        const decimal minorSelfDamage = 3m;
        const decimal minorDrawCards = 1m;
        const int ultimatePotionCount = 3;

        return new CharacterSkillProfile
        {
            CharacterEntry = "silent",
            MinorSkill = new SkillDefinition
            {
                DisplayName = "翻转",
                Description = "抽1张牌。",
                IconLetter = "S",
                CooldownTurns = 7,
                MaxCharges = 2,
                AllowOutsideCombat = false,
                AccentColor = new Color(0.24f, 0.62f, 0.42f),
                OnUseAsync = static async context =>
                {
                    var player = context.Player;
                    if (player == null)
                    {
                        throw new OperationCanceledException("Silent minor skill canceled: player not found.");
                    }

                    await CardPileCmd.Draw(context.ChoiceContext, minorDrawCards, player);
                    Log.Info("[SkillMod] Silent minor skill used: draw 1 card.");
                }
            },
            UltimateSkill = new SkillDefinition
            {
                DisplayName = "制造",
                Description = "获得3瓶随机药水。",
                IconLetter = "N",
                CooldownTurns = 55,
                MaxCharges = 1,
                AllowOutsideCombat = false,
                UltimateChargeOnAttack = 0.4m,
                UltimateChargeOnKill = 2,
                AccentColor = new Color(0.16f, 0.44f, 0.3f),
                OnUseAsync = static async context =>
                {
                    var player = context.Player;
                    if (player == null)
                    {
                        throw new OperationCanceledException("Silent ultimate skill canceled: player not found.");
                    }

                    if (player.RunState == null)
                    {
                        throw new OperationCanceledException("Silent ultimate skill canceled: run state unavailable.");
                    }

                    var playerCombatState = player.PlayerCombatState;
                    if (playerCombatState == null)
                    {
                        throw new OperationCanceledException("Silent ultimate skill canceled: combat state unavailable.");
                    }

                    int energyToSpend = playerCombatState.Energy;
                    // if (energyToSpend > 0)
                    // {
                    //     await PlayerCmd.LoseEnergy(energyToSpend, player);
                    // }

                    for (int i = 0; i < ultimatePotionCount; i++)
                    {
                        await PotionCmd.TryToProcure(PotionFactory.CreateRandomPotionInCombat(player, player.RunState.Rng.CombatPotionGeneration).ToMutable(), player);
                    }

                    Log.Info("[SkillMod] Silent ultimate skill used: spent all energy and attempted to gain 3 random potions.");
                }
            }
        };
    }
}
