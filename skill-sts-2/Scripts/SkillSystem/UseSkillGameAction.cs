using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace skill_sts2.Scripts.SkillSystem;

internal sealed class UseSkillGameAction : GameAction
{
    public override ulong OwnerId => Player.NetId;

    public override GameActionType ActionType
    {
        get
        {
            if (!WasEnqueuedInCombat)
            {
                return GameActionType.NonCombat;
            }

            return GameActionType.CombatPlayPhaseOnly;
        }
    }

    public Player Player { get; }

    public SkillSlot Slot { get; }

    public uint? TargetCombatId { get; }

    public bool WasEnqueuedInCombat { get; }

    public UseSkillGameAction(Player player, SkillSlot slot, uint? targetCombatId, bool wasEnqueuedInCombat)
    {
        Player = player;
        Slot = slot;
        TargetCombatId = targetCombatId;
        WasEnqueuedInCombat = wasEnqueuedInCombat;
    }

    protected override async Task ExecuteAction()
    {
        PlayerChoiceContext choiceContext = new GameActionPlayerChoiceContext(this);
        await CharacterSkillSystem.ExecuteSkillActionFromQueueAsync(Player, Slot, TargetCombatId, choiceContext);
    }

    public override INetAction ToNetAction()
    {
        return new NetUseSkillAction
        {
            slot = (byte)Slot,
            targetCombatId = TargetCombatId,
            enqueuedInCombat = WasEnqueuedInCombat
        };
    }

    public override string ToString()
    {
        return $"UseSkillGameAction owner={OwnerId} slot={Slot} target={TargetCombatId?.ToString() ?? "null"} combat={WasEnqueuedInCombat}";
    }
}

public struct NetUseSkillAction : INetAction, IPacketSerializable
{
    public byte slot;

    public uint? targetCombatId;

    public bool enqueuedInCombat;

    public GameAction ToGameAction(Player player)
    {
        SkillSlot resolvedSlot = slot switch
        {
            0 => SkillSlot.Minor,
            1 => SkillSlot.Ultimate,
            _ => throw new InvalidOperationException($"Invalid skill slot value in NetUseSkillAction: {slot}")
        };

        return new UseSkillGameAction(player, resolvedSlot, targetCombatId, enqueuedInCombat);
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(slot);
        writer.WriteBool(targetCombatId.HasValue);
        if (targetCombatId.HasValue)
        {
            writer.WriteUInt(targetCombatId.Value, 6);
        }

        writer.WriteBool(enqueuedInCombat);
    }

    public void Deserialize(PacketReader reader)
    {
        slot = reader.ReadByte();

        if (reader.ReadBool())
        {
            targetCombatId = reader.ReadUInt(6);
        }
        else
        {
            targetCombatId = null;
        }

        enqueuedInCombat = reader.ReadBool();
    }

    public override string ToString()
    {
        return $"NetUseSkillAction slot={slot} target={targetCombatId?.ToString() ?? "null"} combat={enqueuedInCombat}";
    }
}
