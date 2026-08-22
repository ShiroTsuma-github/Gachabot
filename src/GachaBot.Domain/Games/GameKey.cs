namespace GachaBot.Domain.Games;

public enum GameKey
{
    WutheringWaves = 1,
    NevernessToEverness = 2,
}

public static class GameKeyExtensions
{
    public static string ToSlug(this GameKey game) => game switch
    {
        GameKey.WutheringWaves => "wuthering-waves",
        GameKey.NevernessToEverness => "neverness-to-everness",
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };
}
