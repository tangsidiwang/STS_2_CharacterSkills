using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using skill_sts2.Scripts.SkillSystem;

namespace skill_sts2.Scripts.CharacterConfigs;

public sealed class IroncladSkillProvider : ICharacterSkillProvider
{
	public CharacterSkillProfile Build()
	{
		const decimal minorTempStrength = 2m;
		const decimal minorSelfVulnerable = 2m;
		const decimal ultimateDamage = 22m;
		const decimal ultimateSelfHpLoss = 5m;

		return new CharacterSkillProfile
		{
			CharacterEntry = "ironclad",
			MinorSkill = new SkillDefinition
			{
				DisplayName = "强化",
				Description = "获得2点临时力量",
				IconLetter = "I",
				CooldownTurns = 3,
				MaxCharges = 1,
				AllowOutsideCombat = false,
				AccentColor = new Color(0.83f, 0.35f, 0.28f),
				OnUseAsync = static async context =>
				{
					Creature target = context.Player.Creature;
					await PowerCmd.Apply<FlexPotionPower>(target, minorTempStrength, context.Player.Creature, null);
					// await PowerCmd.Apply<VulnerablePower>(target, minorSelfVulnerable, context.Player.Creature, null);
					
					Log.Info("[SkillMod] Ironclad minor skill used: +2 temporary Strength.");
				}
			},
			UltimateSkill = new SkillDefinition
			{
				DisplayName = "终结者",
				Description = "选择一个敌人。造成22点伤害并使其眩晕。自己失去5点生命。",
				IconLetter = "R",
				CooldownTurns = 70,
				MaxCharges = 1,
				AllowOutsideCombat = false,
				RequiresEnemyTarget = true,
				UltimateChargeOnAttack = 0.6m,
				UltimateChargeOnKill = 1.2m,
				AccentColor = new Color(0.66f, 0.2f, 0.18f),
				OnUseAsync = static async context =>
				{
					Creature? target = await SkillTargetingHelper.ResolveEnemyTargetAsync(context);
					if (target == null)
					{
						throw new OperationCanceledException("Ironclad ultimate targeting canceled.");
					}

					VfxCmd.PlayOnCreature(target, "vfx/vfx_attack_slash");
					await CreatureCmd.Damage(context.ChoiceContext, context.Player.Creature, ultimateSelfHpLoss, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, context.Player.Creature, null);
					await CreatureCmd.Damage(context.ChoiceContext, target, ultimateDamage, ValueProp.Move, context.Player.Creature, null);
					await CreatureCmd.Stun(target);
					Log.Info("[SkillMod] Ironclad ultimate skill used: lose 5 HP, deal 22 damage + stun.");
				}
			}
		};
	}
}