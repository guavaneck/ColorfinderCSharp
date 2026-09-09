using ColorFinder.Core.Rules;

namespace ColorFinder.Core.Tests;

public sealed class JsonColorRuleServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "colorfinder-rule-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RulesPersistAndExtensionsAreNormalized()
    {
        var database = Path.Combine(_root, "rules.json");
        var saved = await new JsonColorRuleService(database).SaveRuleAsync(new("", "PDF", "red", true, 4));
        var rules = await new JsonColorRuleService(database).GetRulesAsync();

        Assert.Equal(".pdf", saved.Extension);
        Assert.Equal(saved, Assert.Single(rules));
    }

    [Fact]
    public async Task HighestPriorityEnabledRuleWins()
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "rules.json"));
        await service.SaveRuleAsync(new("low", ".cs", "green", true, 1));
        await service.SaveRuleAsync(new("disabled", ".cs", "red", false, 20));
        await service.SaveRuleAsync(new("high", ".cs", "blue", true, 10));

        var match = await service.MatchAsync(Path.Combine(_root, "Program.CS"));

        Assert.NotNull(match);
        Assert.Equal("high", match.Id);
        Assert.Equal("blue", match.ColorId);
    }

    [Fact]
    public async Task RulesCanBeEditedAndDeleted()
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "rules.json"));
        await service.SaveRuleAsync(new("rule", ".jpg", "green"));
        await service.SaveRuleAsync(new("rule", ".png", "purple", false, 2));
        await service.DeleteRuleAsync("rule");

        Assert.Empty(await service.GetRulesAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("*.pdf")]
    [InlineData("folder/file")]
    public async Task InvalidExtensionsAreRejected(string extension)
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "rules.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveRuleAsync(new("", extension, "red")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
