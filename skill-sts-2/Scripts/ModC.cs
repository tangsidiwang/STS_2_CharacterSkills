using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using skill_sts2.Scripts.SkillSystem;

namespace skill_sts2.Scripts;

// 必须要加的属性，用于注册 Mod。字符串和初始化函数命名一致。
[ModInitializer("Init")]
public class ModC
{
    // 打 patch（即修改游戏代码的功能）用
    private static Harmony? _harmony;

    // 初始化函数
    public static void Init()
    {
        CharacterSkillSystem.Initialize();

        // 传入参数随意，只要不和其他人撞车即可
        _harmony = new Harmony("sts2.tongs.skill");
        _harmony.PatchAll();

        Log.Debug("Mod initialized!");
    }
}