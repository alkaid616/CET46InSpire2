using STS2RitsuLib.Scaffolding.Content;

namespace CET46InSpire2.Scripts.Cet46.Models;

/// <summary>
/// Internal registration-only pool. It gives RitsuLib ownership for CET/JLPT relics
/// without adding them to vanilla random relic rewards.
/// </summary>
public sealed class Cet46EventRelicPool : TypeListRelicPoolModel
{
    public override string EnergyColorName => "colorless";
}
