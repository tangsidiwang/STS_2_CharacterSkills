using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace skill_sts2.Scripts.SkillSystem;

internal static class SkillTargetingHelper
{
    public static async Task<Creature?> ResolveEnemyTargetAsync(SkillUseContext context)
    {
        if (context.TargetCombatId.HasValue)
        {
            CombatState? combatState = context.CombatState ?? context.Player.Creature.CombatState;
            if (combatState == null)
            {
                return null;
            }

            return await combatState.GetCreatureAsync(context.TargetCombatId.Value, 10.0);
        }

        if (context.TopBar == null)
        {
            return null;
        }

        return await SelectEnemyLikePotionAsync(context.TopBar, context.Player.Creature);
    }

    public static async Task<Creature?> SelectEnemyLikePotionAsync(Control sourceControl, Creature owner)
    {
        if (!CombatManager.Instance.IsInProgress || !GodotObject.IsInstanceValid(sourceControl))
        {
            return null;
        }

        NTargetManager? targetManager = NTargetManager.Instance;
        if (targetManager == null)
        {
            return null;
        }

        bool usingController = NControllerManager.Instance?.IsUsingController ?? false;

        targetManager.StartTargeting(
            TargetType.AnyEnemy,
            sourceControl,
            usingController ? TargetMode.Controller : TargetMode.ClickMouseToTarget,
            ShouldCancelTargeting,
            null);

        if (usingController)
        {
            RestrictControllerToEnemies(owner);
        }

        try
        {
            Node? selected = await targetManager.SelectionFinished();
            if (selected is NCreature selectedCreature)
            {
                return selectedCreature.Entity;
            }

            return null;
        }
        finally
        {
            NCombatRoom.Instance?.EnableControllerNavigation();
        }
    }

    private static void RestrictControllerToEnemies(Creature owner)
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return;
        }

        CombatState? combatState = owner.CombatState;
        if (combatState == null)
        {
            return;
        }

        List<Control> enemyHitboxes = new();
        foreach (Creature enemy in combatState.GetOpponentsOf(owner))
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            NCreature? node = NCombatRoom.Instance?.GetCreatureNode(enemy);
            if (node?.Hitbox != null)
            {
                enemyHitboxes.Add(node.Hitbox);
            }
        }

        if (enemyHitboxes.Count == 0)
        {
            return;
        }

        NCombatRoom.Instance?.RestrictControllerNavigation(enemyHitboxes);
        enemyHitboxes[0].TryGrabFocus();
    }

    private static bool ShouldCancelTargeting()
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return true;
        }

        if (NOverlayStack.Instance?.ScreenCount > 0)
        {
            return true;
        }

        return NCapstoneContainer.Instance?.InUse ?? false;
    }
}
