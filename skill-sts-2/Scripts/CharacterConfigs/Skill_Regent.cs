using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using skill_sts2.Scripts.SkillSystem;

namespace skill_sts2.Scripts.CharacterConfigs;

public sealed class RegentSkillProvider : ICharacterSkillProvider
{
    public CharacterSkillProfile Build()
    {
        const decimal ultimateForgeAmount = 5m;

        return new CharacterSkillProfile
        {
            CharacterEntry = "regent",
            MinorSkill = new SkillDefinition
            {
                DisplayName = "星辰之击",
                Description = "造成等同于辉星数量的伤害。",
                IconLetter = "G",
                CooldownTurns = 4,
                MaxCharges = 3,
                AllowOutsideCombat = false,
                RequiresEnemyTarget = true,
                AccentColor = new Color(0.86f, 0.69f, 0.22f),
                OnUseAsync = static async context =>
                {
                    var player = context.Player;
                    if (player == null)
                    {
                        throw new OperationCanceledException("Regent minor skill canceled: player not found.");
                    }

                    var playerCombatState = player.PlayerCombatState;
                    if (playerCombatState == null)
                    {
                        throw new OperationCanceledException("Regent minor skill canceled: combat state unavailable.");
                    }

                    Creature? target = await SkillTargetingHelper.ResolveEnemyTargetAsync(context);
                    if (target == null)
                    {
                        throw new OperationCanceledException("Regent minor skill targeting canceled.");
                    }

                    decimal starsDamage = playerCombatState.Stars;
                    await CreatureCmd.Damage(context.ChoiceContext, target, starsDamage, ValueProp.Move, player.Creature, null);
                    Log.Info($"[SkillMod] Regent minor skill used: dealt {starsDamage} damage based on stars.");
                }
            },
            UltimateSkill = new SkillDefinition
            {
                DisplayName = "君王之判",
                Description = "锻造5，并获得等同于君王之剑伤害的格挡。",
                IconLetter = "V",
                CooldownTurns = 50,
                MaxCharges = 1,
                AllowOutsideCombat = false,
                UltimateChargeOnAttack = 0.4m,
                UltimateChargeOnKill = 1.5m,
                AccentColor = new Color(0.7f, 0.53f, 0.15f),
                OnUseAsync = static async context =>
                {
                    var player = context.Player;
                    if (player == null)
                    {
                        throw new OperationCanceledException("Regent ultimate skill canceled: player not found.");
                    }

                    IEnumerable<SovereignBlade> blades = await ForgeCmd.Forge(ultimateForgeAmount, player, null);

                    decimal blockAmount = 0m;
                    foreach (SovereignBlade blade in blades)
                    {
                        blockAmount = blade.DynamicVars.Damage.BaseValue;
                        break;
                    }

                    if (blockAmount > 0m)
                    {
                        await CreatureCmd.GainBlock(player.Creature, blockAmount, ValueProp.Unpowered, null);
                    }

                    Log.Info($"[SkillMod] Regent ultimate skill used: forge 5 and gain {blockAmount} block from Sovereign Blade equivalent.");
                }
            }
        };
    }
}
