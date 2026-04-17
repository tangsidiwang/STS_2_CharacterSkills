using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace skill_sts2.Scripts.SkillSystem;

internal static class CharacterSkillSystem
{
    private const string SkillContainerName = "ModCharacterSkillContainer";
    private static readonly object StateLock = new();

    private static IRunState? _currentRun;
    private static readonly Dictionary<ulong, PlayerSkillRuntime> RuntimeByPlayer = new();
    private static Dictionary<ulong, PlayerSkillRuntimeSnapshot>? _loadedSnapshotsByPlayer;

    private static NTopBar? _currentTopBar;
    private static HBoxContainer? _skillContainer;
    private static Button? _minorButton;
    private static Button? _ultimateButton;
    private static NinePatchRect? _minorBg;
    private static NinePatchRect? _ultimateBg;
    private static Label? _minorIconLabel;
    private static Label? _ultimateIconLabel;
    private static Label? _minorStatusLabel;
    private static Label? _ultimateStatusLabel;
    private static Control? _activeSkillHoverTip;
    private static Button? _activeHoverOwner;
    private static SkillSlot _activeHoverSlot;
    private static bool _isHoverTipVisible;

    private static bool _isInitialized;
    private static bool _refreshScheduled;

    private static readonly Texture2D? TopBarButtonBg = GD.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_char_backdrop.tres");
    private static readonly PackedScene? HoverTipScene = GD.Load<PackedScene>("res://scenes/ui/hover_tip.tscn");
    private const string HeaderFontPath = "res://themes/kreon_bold_glyph_space_two.tres";
    private const string StatusFontPath = "res://themes/kreon_regular_glyph_space_one.tres";

    public static void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        CombatManager.Instance.CombatEnded += _ => RequestUiRefresh();
    }

    public static void OnTopBarInitialized(NTopBar topBar, IRunState runState)
    {
        Initialize();
        SetRun(runState);
        _currentTopBar = topBar;

        AttachUi(topBar);
        EnsureLocalPlayerState();
        RefreshUi();
    }

    public static void OnTopBarExited(NTopBar topBar)
    {
        if (!ReferenceEquals(_currentTopBar, topBar))
        {
            return;
        }

        _currentTopBar = null;
        _skillContainer = null;
        _minorButton = null;
        _ultimateButton = null;
        _minorBg = null;
        _ultimateBg = null;
        _minorIconLabel = null;
        _ultimateIconLabel = null;
        _minorStatusLabel = null;
        _ultimateStatusLabel = null;
        HideSkillHoverTip();
    }

    public static void OnNewRunInitialized(IRunState runState, bool isMultiplayer)
    {
        _ = isMultiplayer;
        SetRun(runState);
        _loadedSnapshotsByPlayer = null;
    }

    public static void OnRunLoaded(IRunState runState, SerializableRun save, bool isMultiplayer)
    {
        SetRun(runState);
        _loadedSnapshotsByPlayer = SkillStatePersistence.LoadSnapshots(save, isMultiplayer);
    }

    public static void OnRunSerialized(SerializableRun save, bool isMultiplayer)
    {
        try
        {
            Dictionary<ulong, PlayerSkillRuntimeSnapshot> snapshots = CaptureRuntimeSnapshots();
            if (snapshots.Count == 0)
            {
                return;
            }

            SkillStatePersistence.SaveSnapshots(save, isMultiplayer, snapshots);
        }
        catch (Exception ex)
        {
            Log.Warn($"[SkillMod] Failed to serialize sidecar runtime state: {ex.Message}");
        }
    }

    public static void HandleDamageTask(Task<IEnumerable<DamageResult>> damageTask, Creature? dealer)
    {
        _ = HandleDamageTaskInternal(damageTask, dealer);
    }

    public static async Task TryActivateSkillAsync(SkillSlot slot)
    {
        try
        {
            NTopBar? topBar = _currentTopBar;
            IRunState? runState = _currentRun;

            if (!IsValid(topBar) || runState == null)
            {
                return;
            }

            Player? player = LocalContext.GetMe(runState);
            if (player == null)
            {
                return;
            }

            SetRun(player.RunState);
            PlayerSkillRuntime? runtime = GetOrCreateRuntime(player);
            if (runtime == null)
            {
                return;
            }

            bool isInCombat = CombatManager.Instance.IsInProgress;
            if (!runtime.CanUse(slot, isInCombat))
            {
                RequestUiRefresh();
                return;
            }

            SkillDefinition definition = slot == SkillSlot.Minor ? runtime.Profile.MinorSkill : runtime.Profile.UltimateSkill;
            uint? targetCombatId = null;
            if (definition.RequiresEnemyTarget)
            {
                if (!IsValid(topBar))
                {
                    throw new OperationCanceledException("Skill targeting canceled: top bar UI unavailable.");
                }

                Creature? selected = await SkillTargetingHelper.SelectEnemyLikePotionAsync(topBar!, player.Creature);
                if (!selected?.CombatId.HasValue ?? true)
                {
                    throw new OperationCanceledException("Skill targeting canceled.");
                }

                targetCombatId = selected.CombatId.Value;
            }

            UseSkillGameAction action = new(player, slot, targetCombatId, isInCombat);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            RequestUiRefresh();
        }
        catch (OperationCanceledException)
        {
            RequestUiRefresh();
        }
        catch (Exception ex)
        {
            Log.Error($"[SkillMod] TryActivateSkillAsync failed: {ex}");
        }
    }

    public static async Task ExecuteSkillActionFromQueueAsync(
        Player player,
        SkillSlot slot,
        uint? targetCombatId,
        PlayerChoiceContext choiceContext)
    {
        try
        {
            CharacterSkillProfile? profile = CharacterSkillRegistry.Get(player.Character.Id.Entry);
            if (profile == null)
            {
                return;
            }

            SkillDefinition definition = slot == SkillSlot.Minor ? profile.MinorSkill : profile.UltimateSkill;
            SkillUseContext context = new()
            {
                RunState = player.RunState,
                Player = player,
                TopBar = LocalContext.IsMe(player) ? _currentTopBar : null,
                ChoiceContext = choiceContext,
                TargetCombatId = targetCombatId,
                CombatState = player.Creature.CombatState
            };

            await definition.OnUseAsync(context);

            if (LocalContext.IsMe(player))
            {
                SetRun(player.RunState);
                PlayerSkillRuntime? runtime = GetOrCreateRuntime(player);
                runtime?.TryConsume(slot);
                SfxCmd.Play("event:/sfx/ui/clicks/ui_click");
                RequestUiRefresh();
            }
        }
        catch (OperationCanceledException)
        {
            if (LocalContext.IsMe(player))
            {
                RequestUiRefresh();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[SkillMod] ExecuteSkillActionFromQueueAsync failed: {ex}");
        }
    }

    private static async Task HandleDamageTaskInternal(Task<IEnumerable<DamageResult>> damageTask, Creature? dealer)
    {
        try
        {
            IEnumerable<DamageResult> results = await damageTask;
            OnDamageResolved(results, dealer);
        }
        catch (Exception ex)
        {
            Log.Error($"[SkillMod] Damage hook failed: {ex}");
        }
    }

    private static void OnDamageResolved(IEnumerable<DamageResult> results, Creature? dealer)
    {
        if (dealer == null)
        {
            return;
        }

        Player? sourcePlayer = dealer.Player ?? dealer.PetOwner;
        if (sourcePlayer == null || !LocalContext.IsMe(sourcePlayer))
        {
            return;
        }

        int attacks = 0;
        int kills = 0;
        foreach (DamageResult result in results)
        {
            if (result.Receiver.Side != CombatSide.Enemy)
            {
                continue;
            }

            if (result.TotalDamage > 0)
            {
                attacks++;
            }

            if (result.WasTargetKilled)
            {
                kills++;
            }
        }

        if (attacks <= 0 && kills <= 0)
        {
            return;
        }

        SetRun(sourcePlayer.RunState);
        PlayerSkillRuntime? runtime = GetOrCreateRuntime(sourcePlayer);
        runtime?.ApplyUltimateRechargeFromCombat(attacks, kills);
        RequestUiRefresh();
    }

    private static void OnTurnStarted(CombatState state)
    {
        if (state.CurrentSide != CombatSide.Player)
        {
            return;
        }

        Player? player = LocalContext.GetMe(state);
        if (player == null)
        {
            return;
        }

        SetRun(player.RunState);
        PlayerSkillRuntime? runtime = GetOrCreateRuntime(player);
        runtime?.AdvancePlayerTurn();
        RequestUiRefresh();
    }

    private static void EnsureLocalPlayerState()
    {
        IRunState? runState = _currentRun;
        Player? player = LocalContext.GetMe(runState);
        if (player != null)
        {
            GetOrCreateRuntime(player);
        }
    }

    private static PlayerSkillRuntime? GetOrCreateRuntime(Player player)
    {
        lock (StateLock)
        {
            if (RuntimeByPlayer.TryGetValue(player.NetId, out PlayerSkillRuntime? existing))
            {
                return existing;
            }

            CharacterSkillProfile? profile = CharacterSkillRegistry.Get(player.Character.Id.Entry);
            if (profile == null)
            {
                return null;
            }

            PlayerSkillRuntime runtime = new(profile);
            if (_loadedSnapshotsByPlayer != null
                && _loadedSnapshotsByPlayer.TryGetValue(player.NetId, out PlayerSkillRuntimeSnapshot? snapshot))
            {
                runtime.ApplySnapshot(snapshot);
            }

            RuntimeByPlayer[player.NetId] = runtime;
            return runtime;
        }
    }

    private static void SetRun(IRunState runState)
    {
        lock (StateLock)
        {
            if (ReferenceEquals(_currentRun, runState))
            {
                return;
            }

            _currentRun = runState;
            RuntimeByPlayer.Clear();
            _loadedSnapshotsByPlayer = null;
        }
    }

    private static void RequestUiRefresh()
    {
        if (_refreshScheduled)
        {
            return;
        }

        _refreshScheduled = true;
        RefreshUi();
    }

    private static void RefreshUi()
    {
        _refreshScheduled = false;

        if (!EnsureUiIsAlive())
        {
            return;
        }

        IRunState? runState = _currentRun;
        Player? player = LocalContext.GetMe(runState);
        if (player == null)
        {
            _skillContainer!.Visible = false;
            return;
        }

        PlayerSkillRuntime? runtime = GetOrCreateRuntime(player);
        if (runtime == null)
        {
            _skillContainer!.Visible = false;
            return;
        }

        _skillContainer!.Visible = true;
        bool isInCombat = CombatManager.Instance.IsInProgress;
        UpdateButton(runtime.Profile.MinorSkill, runtime.GetViewState(SkillSlot.Minor, isInCombat), _minorButton!, _minorBg!, _minorIconLabel!, _minorStatusLabel!);
        UpdateButton(runtime.Profile.UltimateSkill, runtime.GetViewState(SkillSlot.Ultimate, isInCombat), _ultimateButton!, _ultimateBg!, _ultimateIconLabel!, _ultimateStatusLabel!);
        RefreshActiveHoverTip();
    }

    private static void UpdateButton(
        SkillDefinition definition,
        SkillViewState state,
        Button button,
        NinePatchRect bg,
        Label icon,
        Label status
    )
    {
        icon.Text = definition.IconLetter;
        status.Text = state.StatusText;

        button.Disabled = !state.CanUse;
        bg.Modulate = state.CanUse ? definition.AccentColor : new Color(0.5f, 0.5f, 0.5f, 0.75f);
        icon.Modulate = state.CanUse ? Colors.White : new Color(0.85f, 0.85f, 0.85f, 0.9f);

        // Disable the default OS tooltip because we show a styled in-game hover tip.
        button.TooltipText = string.Empty;
    }

    private static void AttachUi(NTopBar topBar)
    {
        if (!IsValid(topBar))
        {
            return;
        }

        HBoxContainer leftAligned = topBar.GetNodeOrNull<HBoxContainer>("LeftAlignedStuff");
        HBoxContainer roomIcons = topBar.GetNodeOrNull<HBoxContainer>("LeftAlignedStuff/RoomIcons");

        if (!IsValid(leftAligned) || !IsValid(roomIcons))
        {
            Log.Error("[SkillMod] Could not find LeftAlignedStuff/RoomIcons in top bar.");
            return;
        }

        Node? existing = leftAligned.GetNodeOrNull<Node>(SkillContainerName);
        if (existing != null)
        {
            existing.QueueFree();
        }

        _skillContainer = new HBoxContainer
        {
            Name = SkillContainerName,
            CustomMinimumSize = new Vector2(0f, 80f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _skillContainer.AddThemeConstantOverride("separation", 8);

        _minorButton = BuildButton(out _minorBg, out _minorIconLabel, out _minorStatusLabel);
        _ultimateButton = BuildButton(out _ultimateBg, out _ultimateIconLabel, out _ultimateStatusLabel);

        _minorButton.Pressed += () => TaskHelper.RunSafely(TryActivateSkillAsync(SkillSlot.Minor));
        _ultimateButton.Pressed += () => TaskHelper.RunSafely(TryActivateSkillAsync(SkillSlot.Ultimate));
        BindHoverTipEvents(_minorButton, SkillSlot.Minor);
        BindHoverTipEvents(_ultimateButton, SkillSlot.Ultimate);

        _skillContainer.AddChild(_minorButton);
        _skillContainer.AddChild(_ultimateButton);
        leftAligned.AddChild(_skillContainer);
        leftAligned.MoveChild(_skillContainer, roomIcons.GetIndex() + 1);
    }

    private static Button BuildButton(out NinePatchRect bg, out Label iconLabel, out Label statusLabel)
    {
        Button button = new()
        {
            Flat = true,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(74f, 80f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Text = string.Empty
        };

        bg = new NinePatchRect
        {
            Texture = TopBarButtonBg,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.PatchMarginLeft = 32;
        bg.PatchMarginTop = 32;
        bg.PatchMarginRight = 32;
        bg.PatchMarginBottom = 32;
        button.AddChild(bg);

        iconLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = "A"
        };
        iconLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        iconLabel.OffsetLeft = -26f;
        iconLabel.OffsetRight = 26f;
        iconLabel.OffsetTop = 10f;
        iconLabel.OffsetBottom = 48f;
        iconLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9647f, 0.8862f));
        iconLabel.AddThemeColorOverride("font_outline_color", new Color(0.098f, 0.1607f, 0.1882f));
        iconLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.125f));
        iconLabel.AddThemeConstantOverride("outline_size", 8);
        iconLabel.AddThemeConstantOverride("shadow_offset_x", 3);
        iconLabel.AddThemeConstantOverride("shadow_offset_y", 2);
        iconLabel.AddThemeFontSizeOverride("font_size", 26);
        TryApplyThemeFontOverride(iconLabel, HeaderFontPath);
        button.AddChild(iconLabel);

        statusLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = "READY"
        };
        statusLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        statusLabel.OffsetLeft = -34f;
        statusLabel.OffsetRight = 34f;
        statusLabel.OffsetTop = -22f;
        statusLabel.OffsetBottom = -2f;
        statusLabel.AddThemeColorOverride("font_color", new Color(0.7725f, 0.8549f, 0.8078f));
        statusLabel.AddThemeColorOverride("font_outline_color", new Color(0.098f, 0.1607f, 0.1882f));
        statusLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.125f));
        statusLabel.AddThemeConstantOverride("outline_size", 6);
        statusLabel.AddThemeConstantOverride("shadow_offset_x", 2);
        statusLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        statusLabel.AddThemeFontSizeOverride("font_size", 14);
        TryApplyThemeFontOverride(statusLabel, StatusFontPath);
        button.AddChild(statusLabel);

        return button;
    }

    private static void TryApplyThemeFontOverride(Control control, string fontPath)
    {
        try
        {
            Font? font = GD.Load<Font>(fontPath);
            if (IsValid(font))
            {
                control.AddThemeFontOverride("font", font);
            }
        }
        catch (ObjectDisposedException)
        {
            // Ignore stale font resources and keep default theme font.
        }
    }

    private static void BindHoverTipEvents(Button button, SkillSlot slot)
    {
        button.MouseEntered += () => ShowSkillHoverTip(button, slot);
        button.FocusEntered += () => ShowSkillHoverTip(button, slot);
        button.MouseExited += HideSkillHoverTip;
        button.FocusExited += HideSkillHoverTip;
    }

    private static void RefreshActiveHoverTip()
    {
        if (!_isHoverTipVisible || !IsValid(_activeHoverOwner))
        {
            return;
        }

        ShowSkillHoverTip(_activeHoverOwner!, _activeHoverSlot);
    }

    private static void ShowSkillHoverTip(Button owner, SkillSlot slot)
    {
        NGame? game = NGame.Instance;
        if (!IsValid(owner) || HoverTipScene == null || game == null)
        {
            return;
        }

        Node? hoverTipsContainer = game.HoverTipsContainer;
        if (hoverTipsContainer == null)
        {
            return;
        }

        IRunState? runState = _currentRun;
        Player? player = LocalContext.GetMe(runState);
        if (player == null)
        {
            return;
        }

        PlayerSkillRuntime? runtime = GetOrCreateRuntime(player);
        if (runtime == null)
        {
            return;
        }

        bool isInCombat = CombatManager.Instance.IsInProgress;
        SkillDefinition definition = slot == SkillSlot.Minor ? runtime.Profile.MinorSkill : runtime.Profile.UltimateSkill;
        SkillViewState viewState = runtime.GetViewState(slot, isInCombat);

        HideSkillHoverTip();

        Control tipRoot = HoverTipScene.Instantiate<Control>(PackedScene.GenEditState.Disabled);
        Label title = tipRoot.GetNode<Label>("%Title");
        title.Visible = true;
        title.Text = definition.DisplayName;

        TextureRect icon = tipRoot.GetNode<TextureRect>("%Icon");
        icon.Visible = false;

        RichTextLabel description = tipRoot.GetNode<RichTextLabel>("%Description");
        description.Text = BuildSkillHoverTipBody(definition, viewState);

        hoverTipsContainer.AddChildSafely(tipRoot);
        tipRoot.ResetSize();
        PositionHoverTip(tipRoot, owner, game.GetViewportRect().Size);

        _activeSkillHoverTip = tipRoot;
        _activeHoverOwner = owner;
        _activeHoverSlot = slot;
        _isHoverTipVisible = true;
    }

    private static string BuildSkillHoverTipBody(SkillDefinition definition, SkillViewState state)
    {
        string effectText = string.IsNullOrWhiteSpace(definition.Description)
            ? "No description configured."
            : definition.Description;

        return $"{effectText}\n\nStatus: {state.StatusText}\nCooldown: {definition.CooldownTurns}\nCharges: {state.Charges}/{state.MaxCharges}";
    }

    private static void PositionHoverTip(Control tipRoot, Control owner, Vector2 viewport)
    {
        Vector2 desired = owner.GlobalPosition + new Vector2(owner.Size.X + 8f, 0f);
        if (desired.X + tipRoot.Size.X > viewport.X - 8f)
        {
            desired.X = owner.GlobalPosition.X - tipRoot.Size.X - 8f;
        }

        if (desired.Y + tipRoot.Size.Y > viewport.Y - 8f)
        {
            desired.Y = viewport.Y - tipRoot.Size.Y - 8f;
        }

        if (desired.Y < 8f)
        {
            desired.Y = 8f;
        }

        tipRoot.GlobalPosition = desired;
    }

    private static void HideSkillHoverTip()
    {
        if (IsValid(_activeSkillHoverTip))
        {
            _activeSkillHoverTip!.QueueFree();
        }

        _activeSkillHoverTip = null;
        _activeHoverOwner = null;
        _isHoverTipVisible = false;
    }

    private static Dictionary<ulong, PlayerSkillRuntimeSnapshot> CaptureRuntimeSnapshots()
    {
        Dictionary<ulong, PlayerSkillRuntimeSnapshot> snapshots = new();
        lock (StateLock)
        {
            foreach ((ulong netId, PlayerSkillRuntime runtime) in RuntimeByPlayer)
            {
                snapshots[netId] = runtime.ToSnapshot();
            }
        }

        return snapshots;
    }

    private static bool EnsureUiIsAlive()
    {
        if (!IsValid(_skillContainer)
            || !IsValid(_minorButton)
            || !IsValid(_ultimateButton)
            || !IsValid(_minorBg)
            || !IsValid(_ultimateBg)
            || !IsValid(_minorIconLabel)
            || !IsValid(_ultimateIconLabel)
            || !IsValid(_minorStatusLabel)
            || !IsValid(_ultimateStatusLabel))
        {
            return false;
        }

        return true;
    }

    private static bool IsValid(GodotObject? obj)
    {
        return obj != null && GodotObject.IsInstanceValid(obj);
    }
}
