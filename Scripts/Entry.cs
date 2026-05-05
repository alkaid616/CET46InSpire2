using CET46InSpire2.Scripts.Cet46.Bootstrap;
using MegaCrit.Sts2.Core.Modding;

namespace CET46InSpire2.Scripts;

/// <summary>
/// STS2 模组入口。游戏加载程序集时会调用这里完成 RitsuLib 注册和模组启动。
/// </summary>
[ModInitializer(nameof(Init))]
public static class Entry
{
    /// <summary>
    /// 安装所有 RitsuLib patch，注册自定义内容和运行时服务。
    /// </summary>
    public static void Init()
    {
        Cet46Bootstrap.Initialize(typeof(Entry).Assembly);
    }
}
