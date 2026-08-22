using GachaBot.Domain.Content;

namespace GachaBot.Domain.UnitTests;

public sealed class ContentDocumentTests
{
    [Fact]
    public void Create_WithEquivalentBlocks_ProducesStableHash()
    {
        var first = ContentDocument.Create(
        [
            new HeadingBlock("Version 2.6", 1),
            new TextBlock("Maintenance starts soon.", 2),
            new LinkBlock("Patch notes", new Uri("https://example.com/patch"), 3),
            new ImageBlock(new Uri("https://cdn.example.com/banner.webp"), "Patch banner", 4),
        ]);
        var second = ContentDocument.Create(first.Blocks.Reverse());

        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal([1, 2, 3, 4], first.Blocks.Select(block => block.Position));
    }

    [Fact]
    public void Create_WithDuplicatePositions_Throws()
    {
        var blocks = new ContentBlock[]
        {
            new TextBlock("First", 1),
            new TextBlock("Second", 1),
        };

        Assert.Throws<DomainValidationException>(() => ContentDocument.Create(blocks));
    }

    [Fact]
    public void ImageBlock_WithNonHttpUri_Throws()
    {
        Assert.Throws<DomainValidationException>(
            () => new ImageBlock(new Uri("file:///secret.png"), "Unsafe image", 1));
    }
}
