namespace ColorFinder.Core.Rules;

public interface IColorRuleService
{
    Task<IReadOnlyList<ColorRule>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task<ColorRule> SaveRuleAsync(ColorRule rule, CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(string id, CancellationToken cancellationToken = default);
    Task<ColorRule?> MatchAsync(string path, CancellationToken cancellationToken = default);
}
