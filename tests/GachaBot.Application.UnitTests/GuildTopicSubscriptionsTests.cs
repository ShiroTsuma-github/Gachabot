using GachaBot.Application.Publishing;
using GachaBot.Domain.Content;
using GachaBot.Domain.Games;

namespace GachaBot.Application.UnitTests;

public sealed class GuildTopicSubscriptionsTests
{
    [Fact]
    public void SubscribesTo_UsesSubjectSelectionForTheSpecificGame()
    {
        var destination = new GuildDestination(
            1,
            2,
            3,
            true,
            GuildDestinationGames.All,
            DateTimeOffset.UtcNow,
            TopicSubscriptions: new HashSet<GuildTopicSubscription>
            {
                new GuildTopicSubscription(GameKey.WutheringWaves, ContentKind.Event),
                new GuildTopicSubscription(GameKey.NevernessToEverness, ContentKind.RedeemCode),
            });

        Assert.True(destination.SubscribesTo(GameKey.WutheringWaves, ContentKind.Event));
        Assert.False(destination.SubscribesTo(GameKey.WutheringWaves, ContentKind.RedeemCode));
        Assert.True(destination.SubscribesTo(GameKey.NevernessToEverness, ContentKind.RedeemCode));
        Assert.False(destination.SubscribesTo(GameKey.NevernessToEverness, ContentKind.Event));
    }

    [Fact]
    public void SubscribesTo_TreatsExistingDestinationsWithoutTopicRowsAsAllSubjects()
    {
        var destination = new GuildDestination(
            1,
            2,
            3,
            true,
            GuildDestinationGames.All,
            DateTimeOffset.UtcNow);

        Assert.True(destination.SubscribesTo(GameKey.WutheringWaves, ContentKind.Announcement));
        Assert.True(destination.SubscribesTo(GameKey.NevernessToEverness, ContentKind.Event));
    }
}
