namespace skill_sts2.Scripts.SkillSystem;

internal readonly record struct SkillViewState(
    bool CanUse,
    int Charges,
    int MaxCharges,
    int RechargeTurnsRemaining,
    string StatusText
);

internal sealed class SkillRuntimeSnapshot
{
    public int Charges { get; set; }

    public decimal RechargeTurnsRemaining { get; set; }
}

internal sealed class PlayerSkillRuntimeSnapshot
{
    public SkillRuntimeSnapshot Minor { get; set; } = new();

    public SkillRuntimeSnapshot Ultimate { get; set; } = new();
}

internal sealed class SkillRuntimeState
{
    private readonly SkillDefinition _definition;
    private decimal _rechargeTurnsRemaining;

    public int Charges { get; private set; }

    public int RechargeTurnsRemaining => (int)Math.Ceiling(Math.Max(0m, _rechargeTurnsRemaining));

    public bool CanUse => Charges > 0;

    public SkillRuntimeState(SkillDefinition definition, bool startAtMaxCharges = true)
    {
        _definition = definition;

        if (startAtMaxCharges || _definition.CooldownTurns <= 0)
        {
            Charges = MaxCharges;
            _rechargeTurnsRemaining = 0m;
            return;
        }

        Charges = 0;
        _rechargeTurnsRemaining = Math.Max(1, _definition.CooldownTurns);
    }

    public int MaxCharges => Math.Max(1, _definition.MaxCharges);

    public bool TryConsumeUse()
    {
        if (!CanUse)
        {
            return false;
        }

        Charges = Math.Max(0, Charges - 1);
        if (Charges < MaxCharges)
        {
            StartRechargeIfNeeded();
        }

        return true;
    }

    public void AdvanceTurn()
    {
        if (_definition.CooldownTurns <= 0)
        {
            Charges = MaxCharges;
            _rechargeTurnsRemaining = 0m;
            return;
        }

        if (Charges >= MaxCharges)
        {
            _rechargeTurnsRemaining = 0m;
            return;
        }

        if (_rechargeTurnsRemaining > 0m)
        {
            _rechargeTurnsRemaining = Math.Max(0m, _rechargeTurnsRemaining - 1m);
        }

        TryGrantChargeWhenReady();
    }

    public void ReduceRecharge(decimal turns)
    {
        if (turns <= 0m || Charges >= MaxCharges)
        {
            return;
        }

        if (_definition.CooldownTurns <= 0)
        {
            Charges = MaxCharges;
            _rechargeTurnsRemaining = 0m;
            return;
        }

        StartRechargeIfNeeded();
        while (turns > 0m && Charges < MaxCharges)
        {
            if (_rechargeTurnsRemaining <= 0m)
            {
                _rechargeTurnsRemaining = Math.Max(1, _definition.CooldownTurns);
            }

            decimal spent = Math.Min(turns, _rechargeTurnsRemaining);
            _rechargeTurnsRemaining -= spent;
            turns -= spent;

            if (!TryGrantChargeWhenReady())
            {
                break;
            }
        }
    }

    public SkillViewState GetViewState(bool isInCombat, bool allowOutsideCombat)
    {
        bool canUseNow = CanUse && (allowOutsideCombat || isInCombat);
        string statusText;

        if (!allowOutsideCombat && !isInCombat)
        {
            statusText = "BATTLE";
        }
        else if (CanUse)
        {
            statusText = MaxCharges > 1 ? $"{Charges}/{MaxCharges}" : "READY";
        }
        else
        {
            statusText = $"CD {RechargeTurnsRemaining}";
        }

        return new SkillViewState(canUseNow, Charges, MaxCharges, RechargeTurnsRemaining, statusText);
    }

    public SkillRuntimeSnapshot ToSnapshot()
    {
        return new SkillRuntimeSnapshot
        {
            Charges = Charges,
            RechargeTurnsRemaining = _rechargeTurnsRemaining
        };
    }

    public void ApplySnapshot(SkillRuntimeSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        Charges = Math.Clamp(snapshot.Charges, 0, MaxCharges);

        if (_definition.CooldownTurns <= 0)
        {
            Charges = MaxCharges;
            _rechargeTurnsRemaining = 0m;
            return;
        }

        if (Charges >= MaxCharges)
        {
            _rechargeTurnsRemaining = 0m;
            return;
        }

        _rechargeTurnsRemaining = Math.Max(0m, snapshot.RechargeTurnsRemaining);
        if (_rechargeTurnsRemaining <= 0m)
        {
            _rechargeTurnsRemaining = Math.Max(1, _definition.CooldownTurns);
        }
    }

    private void StartRechargeIfNeeded()
    {
        if (RechargeTurnsRemaining > 0)
        {
            return;
        }

        if (_definition.CooldownTurns <= 0)
        {
            Charges = MaxCharges;
            _rechargeTurnsRemaining = 0m;
            return;
        }

        _rechargeTurnsRemaining = Math.Max(1, _definition.CooldownTurns);
    }

    private bool TryGrantChargeWhenReady()
    {
        if (_rechargeTurnsRemaining > 0m)
        {
            return true;
        }

        if (Charges >= MaxCharges)
        {
            _rechargeTurnsRemaining = 0m;
            return false;
        }

        Charges++;
        if (Charges < MaxCharges)
        {
            _rechargeTurnsRemaining = Math.Max(1, _definition.CooldownTurns);
        }
        else
        {
            _rechargeTurnsRemaining = 0m;
        }

        return true;
    }
}

internal sealed class PlayerSkillRuntime
{
    public CharacterSkillProfile Profile { get; }

    private SkillRuntimeState MinorRuntime { get; }

    private SkillRuntimeState UltimateRuntime { get; }

    public PlayerSkillRuntime(CharacterSkillProfile profile)
    {
        Profile = profile;
        MinorRuntime = new SkillRuntimeState(profile.MinorSkill);
        UltimateRuntime = new SkillRuntimeState(profile.UltimateSkill, startAtMaxCharges: false);
    }

    public void AdvancePlayerTurn()
    {
        MinorRuntime.AdvanceTurn();
        UltimateRuntime.AdvanceTurn();
    }

    public void ApplyUltimateRechargeFromCombat(int attacks, int kills)
    {
        if (attacks <= 0 && kills <= 0)
        {
            return;
        }

        decimal gain = attacks * Math.Max(0m, Profile.UltimateSkill.UltimateChargeOnAttack)
            + kills * Math.Max(0m, Profile.UltimateSkill.UltimateChargeOnKill);

        if (gain > 0m)
        {
            UltimateRuntime.ReduceRecharge(gain);
        }
    }

    public SkillViewState GetViewState(SkillSlot slot, bool isInCombat)
    {
        return slot == SkillSlot.Minor
            ? MinorRuntime.GetViewState(isInCombat, Profile.MinorSkill.AllowOutsideCombat)
            : UltimateRuntime.GetViewState(isInCombat, Profile.UltimateSkill.AllowOutsideCombat);
    }

    public bool CanUse(SkillSlot slot, bool isInCombat)
    {
        SkillDefinition definition = slot == SkillSlot.Minor ? Profile.MinorSkill : Profile.UltimateSkill;
        SkillRuntimeState runtime = slot == SkillSlot.Minor ? MinorRuntime : UltimateRuntime;
        return runtime.CanUse && (definition.AllowOutsideCombat || isInCombat);
    }

    public bool TryConsume(SkillSlot slot)
    {
        return slot == SkillSlot.Minor ? MinorRuntime.TryConsumeUse() : UltimateRuntime.TryConsumeUse();
    }

    public PlayerSkillRuntimeSnapshot ToSnapshot()
    {
        return new PlayerSkillRuntimeSnapshot
        {
            Minor = MinorRuntime.ToSnapshot(),
            Ultimate = UltimateRuntime.ToSnapshot()
        };
    }

    public void ApplySnapshot(PlayerSkillRuntimeSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        MinorRuntime.ApplySnapshot(snapshot.Minor);
        UltimateRuntime.ApplySnapshot(snapshot.Ultimate);
    }
}
