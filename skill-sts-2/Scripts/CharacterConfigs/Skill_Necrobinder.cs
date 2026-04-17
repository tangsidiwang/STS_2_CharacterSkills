using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Powers;
using skill_sts2.Scripts.SkillSystem;

namespace skill_sts2.Scripts.CharacterConfigs;

public sealed class NecrobinderSkillProvider : ICharacterSkillProvider
{
    public CharacterSkillProfile Build()
    {
        const decimal minorDoomAmount = 3m;
        const decimal ultimateSummonAmount = 8m;
        const decimal ultimateEnemyStrengthLoss = 8m;

        return new CharacterSkillProfile
        {
            CharacterEntry = "necrobinder",
            MinorSkill = new SkillDefinition
            {
                DisplayName = "灾厄之触",
                Description = "使所有敌人获得3层灾厄，并杀死所有灾厄大于生命值的怪物。",
                IconLetter = "T",
                CooldownTurns = 4,
                MaxCharges = 2,
                AllowOutsideCombat = false,
                AccentColor = new Color(0.56f, 0.41f, 0.7f),
                OnUseAsync = static async context =>
                {
                    Creature self = context.Player.Creature;
                    var combat = self.CombatState;
                    if (combat == null)
                    {
                        return;
                    }

                    List<Creature> enemies = combat.GetOpponentsOf(self).Where(e => e.IsAlive).ToList();
                    List<Creature> overDoomedEnemies = new();
                    foreach (Creature enemy in enemies)
                    {
                        await PowerCmd.Apply<DoomPower>(enemy, minorDoomAmount, self, null);
                        DoomPower? doomPower = enemy.GetPower<DoomPower>();
                        if (doomPower != null && doomPower.Amount > enemy.CurrentHp)
                        {
                            overDoomedEnemies.Add(enemy);
                        }
                    }
                    await DoomPower.DoomKill(overDoomedEnemies);

                    Log.Info("[SkillMod] Necrobinder minor skill used: applied 3 Doom to all enemies and killed over-doomed enemies.");
                }
            },
            UltimateSkill = new SkillDefinition
            {
                DisplayName = "亡灵缚者",
                Description = "召唤8，并使所有敌人减少8点力量一回合。",
                IconLetter = "G",
                CooldownTurns = 50,
                MaxCharges = 1,
                AllowOutsideCombat = false,
                UltimateChargeOnAttack = 0.2m,
                UltimateChargeOnKill = 5,
                AccentColor = new Color(0.41f, 0.26f, 0.55f),
                OnUseAsync = static async context =>
                {
                    Creature self = context.Player.Creature;

                    await OstyCmd.Summon(context.ChoiceContext, context.Player, ultimateSummonAmount, null);

                    var combat = self.CombatState;
                    if (combat != null)
                    {
                        List<Creature> enemies = combat.GetOpponentsOf(self).Where(e => e.IsAlive).ToList();

                        foreach (Creature enemy in enemies)
                        {
                            await PowerCmd.Apply<PiercingWailPower>(enemy, ultimateEnemyStrengthLoss, self, null);
                        }
                    }

                    Log.Info("[SkillMod] Necrobinder ultimate skill used: summon 8 and apply temporary -8 Strength to all enemies.");
                }
            }
        };
    }
}
