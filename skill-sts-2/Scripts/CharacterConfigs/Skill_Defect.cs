using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using skill_sts2.Scripts.SkillSystem;

namespace skill_sts2.Scripts.CharacterConfigs;

public sealed class DefectSkillProvider : ICharacterSkillProvider
{
    public CharacterSkillProfile Build()
    {
        const decimal minorTemporaryFocus = 1m;
        const decimal ultimateAoeDamage = 10m;

        return new CharacterSkillProfile
        {
            CharacterEntry = "defect",
            MinorSkill = new SkillDefinition
            {
                DisplayName = "源",
                Description = "获得1点临时集中。",
                IconLetter = "D",
                CooldownTurns = 6,
                MaxCharges = 3,
                AllowOutsideCombat = false,
                AccentColor = new Color(0.31f, 0.56f, 0.86f),
                OnUseAsync = static async context =>
                {
                    var player = context.Player;
                    if (player == null)
                    {
                        throw new OperationCanceledException("Defect minor skill canceled: player not found.");
                    }

                    var playerCombatState = player.PlayerCombatState;
                    if (playerCombatState == null)
                    {
                        throw new OperationCanceledException("Defect minor skill canceled: combat state unavailable.");
                    }

                    await PowerCmd.Apply<HotfixPower>(player.Creature, minorTemporaryFocus, player.Creature, null);
                    Log.Info("[SkillMod] Defect minor skill used: gained 1 temporary Focus.");
                }
            },
            UltimateSkill = new SkillDefinition
            {
                DisplayName = "启动",
                Description = "触发所有充能球的被动一次，并对全体敌人造成10点伤害。",
                IconLetter = "C",
                CooldownTurns = 50,
                MaxCharges = 1,
                AllowOutsideCombat = false,
                UltimateChargeOnAttack = 0.5m,
                UltimateChargeOnKill = 2,
                AccentColor = new Color(0.2f, 0.39f, 0.67f),
                OnUseAsync = static async context =>
                {
                    var player = context.Player;
                    if (player == null)
                    {
                        throw new OperationCanceledException("Defect ultimate skill canceled: player not found.");
                    }

                    var playerCombatState = player.PlayerCombatState;
                    if (playerCombatState == null)
                    {
                        throw new OperationCanceledException("Defect ultimate skill canceled: combat state unavailable.");
                    }

                    var orbs = playerCombatState.OrbQueue.Orbs.ToList();
                    foreach (var orb in orbs)
                    {
                        await OrbCmd.Passive(context.ChoiceContext, orb, null);
                    }

                    Creature self = player.Creature;
                    var combat = self.CombatState;
                    if (combat != null)
                    {
                        List<Creature> enemies = combat.GetOpponentsOf(self).Where(e => e.IsAlive).ToList();
                        if (enemies.Count > 0)
                        {
                            await CreatureCmd.Damage(context.ChoiceContext, enemies, ultimateAoeDamage, ValueProp.Move, self, null);
                        }
                    }

                    Log.Info("[SkillMod] Defect ultimate skill used: triggered all orb passives once and dealt 10 AoE damage.");
                }
            }
        };
    }
}
