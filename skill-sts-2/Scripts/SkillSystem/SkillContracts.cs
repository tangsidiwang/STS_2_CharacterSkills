using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;

namespace skill_sts2.Scripts.SkillSystem;

public enum SkillSlot
{
    Minor,
    Ultimate
}

public sealed class SkillUseContext
{
    public required IRunState RunState { get; init; }

    public required Player Player { get; init; }

    public NTopBar? TopBar { get; init; }

    public required PlayerChoiceContext ChoiceContext { get; init; }

    public uint? TargetCombatId { get; init; }

    public CombatState? CombatState { get; init; }
}

public sealed class SkillDefinition
{
    public required string DisplayName { get; init; }

    public string Description { get; init; } = string.Empty;

    public required string IconLetter { get; init; }

    public int CooldownTurns { get; init; } = 3;

    public int MaxCharges { get; init; } = 1;

    public bool AllowOutsideCombat { get; init; }

    public bool RequiresEnemyTarget { get; init; }

    public decimal UltimateChargeOnAttack { get; init; }

    public decimal UltimateChargeOnKill { get; init; }

    public Color AccentColor { get; init; } = new Color(0.86f, 0.74f, 0.52f);

    public required Func<SkillUseContext, Task> OnUseAsync { get; init; }
}

public sealed class CharacterSkillProfile
{
    public required string CharacterEntry { get; init; }

    public required SkillDefinition MinorSkill { get; init; }

    public required SkillDefinition UltimateSkill { get; init; }
}

public interface ICharacterSkillProvider
{
    CharacterSkillProfile Build();
}
