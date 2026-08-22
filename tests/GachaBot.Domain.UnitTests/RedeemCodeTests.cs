using GachaBot.Domain.Content;
using GachaBot.Domain.Games;

namespace GachaBot.Domain.UnitTests;

public sealed class RedeemCodeTests
{
    [Fact]
    public void Create_NormalizesCodeAndIdentity()
    {
        var code = RedeemCode.Create(
            GameKey.WutheringWaves,
            "  wuwa-2026  ",
            new Uri("https://wutheringwaves.kurogames.com/"),
            DateTimeOffset.Parse("2026-08-20T10:00:00Z", CultureInfo.InvariantCulture));

        Assert.Equal("WUWA-2026", code.Code);
        Assert.Equal("wuthering-waves:WUWA-2026", code.Identity);
        Assert.True(code.IsActiveAt(DateTimeOffset.Parse("2026-08-19T10:00:00Z", CultureInfo.InvariantCulture)));
        Assert.False(code.IsActiveAt(DateTimeOffset.Parse("2026-08-21T10:00:00Z", CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankCode_Throws(string value)
    {
        Assert.Throws<DomainValidationException>(
            () => RedeemCode.Create(GameKey.NevernessToEverness, value, new Uri("https://example.com")));
    }
}
