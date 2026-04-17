# Skill Mod 配置说明

本文基于当前代码结构，说明如何配置：

- 技能描述（鼠标悬浮卡片，药水风格）
- 技能效果逻辑
- 冷却/储存/绝招回充
- 单体选敌与 AOE

## 1. 当前文件结构

角色技能配置文件：

- `Scripts/Skill_Ironclad.cs`
- `Scripts/CharacterConfigs/Skill_Silent.cs`
- `Scripts/CharacterConfigs/Skill_Defect.cs`
- `Scripts/CharacterConfigs/Skill_Regent.cs`
- `Scripts/CharacterConfigs/Skill_Necrobinder.cs`

系统核心：

- `Scripts/SkillSystem/SkillContracts.cs`: 技能配置结构
- `Scripts/SkillSystem/SkillRuntime.cs`: 冷却与储存运行时
- `Scripts/SkillSystem/CharacterSkillSystem.cs`: top bar 按钮、点击释放、悬浮提示
- `Scripts/SkillSystem/SkillTargetingHelper.cs`: 药水式选敌工具
- `Scripts/SkillSystem/CharacterSkillRegistry.cs`: 角色配置注册

## 2. 技能配置字段

每个技能是一个 `SkillDefinition`，常用字段：

- `DisplayName`: 悬浮卡片标题
- `Description`: 悬浮卡片描述正文（你要写的技能说明）
- `IconLetter`: top bar 按钮字母占位
- `CooldownTurns`: 每次恢复 1 层所需回合数
- `MaxCharges`: 可储存层数（`1`=不可储存）
- `AllowOutsideCombat`: 是否允许非战斗释放
- `AccentColor`: 按钮主色
- `OnUseAsync`: 技能效果实现

绝招常用附加字段：

- `UltimateChargeOnAttack`: 每次攻击减少绝招冷却的回合数
- `UltimateChargeOnKill`: 每次击杀减少绝招冷却的回合数

说明：这两个字段支持小数（`decimal`），例如 `0.5m`。

## 3. 悬浮描述怎么写

技能按钮的悬浮卡片样式已改为游戏内 `hover_tip` 风格（和药水同系视觉）。

你只需要在角色配置里填写：

```csharp
new SkillDefinition
{
   DisplayName = "Crimson Finisher",
   Description = "Choose an enemy. Deal 22 damage and Stun it.",
   ...
}
```

悬浮卡片会自动附加运行时信息：

- `Status`（READY/CD/BATTLE/层数）
- `Cooldown`
- `Charges`

## 4. 效果代码怎么写

所有效果都写在 `OnUseAsync` 里。

`context` 可用对象：

- `context.Player`: 当前玩家
- `context.RunState`: 当前 Run
- `context.TopBar`: top bar 节点
- `context.CombatState`: 当前战斗（可能为 `null`）

注意：

- `OnUseAsync` 正常执行完毕后，系统才会消耗层数并进入冷却
- 抛出 `OperationCanceledException` 会被视为“取消施放”（不消耗）

## 5. 选敌与 AOE

药水式单体选敌（可选，不是强制）：

```csharp
Creature? target = await SkillTargetingHelper.SelectEnemyLikePotionAsync(context.TopBar, context.Player.Creature);
if (target == null)
{
   throw new OperationCanceledException();
}
```

AOE 不需要选敌，直接遍历敌人：

```csharp
Creature self = context.Player.Creature;
var combat = self.CombatState;
if (combat == null) return;

var enemies = combat.GetOpponentsOf(self).Where(e => e.IsAlive).ToList();
await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), enemies, 12m, ValueProp.Move, self, null);
```

## 6. 冷却与储存规则

当前系统规则：

- 冷却跨战斗保留
- 回合推进时机：玩家回合开始
- 每恢复 1 层需要 `CooldownTurns` 回合
- `MaxCharges > 1` 时可储存多层
- 绝招可由攻击/击杀回充（按对应字段）

## 7. 新增或修改角色技能

1. 修改已有角色：直接改对应 `Skill_*.cs` 文件。
2. 新增角色文件：实现 `ICharacterSkillProvider`。
3. 在 `CharacterSkillRegistry.cs` 中注册新 provider。

## 8. 推荐配置流程

1. 先填 `DisplayName` 和 `Description`。
2. 设定 `CooldownTurns`、`MaxCharges`、回充数值。
3. 实现 `OnUseAsync`（先写最小效果，再迭代复杂逻辑）。
4. 进游戏验证：可用性、取消选敌、跨战斗冷却是否符合预期。
