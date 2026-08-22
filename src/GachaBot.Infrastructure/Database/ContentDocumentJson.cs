using System.Text.Json;
using GachaBot.Domain.Content;

namespace GachaBot.Infrastructure.Database;

public static class ContentDocumentJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(ContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var blocks = document.Blocks.Select(ToDto).ToArray();
        return JsonSerializer.Serialize(blocks, SerializerOptions);
    }

    public static ContentDocument Deserialize(string json)
    {
        var blocks = JsonSerializer.Deserialize<BlockDto[]>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Stored content document is invalid.");
        return ContentDocument.Create(blocks.Select(FromDto));
    }

    private static BlockDto ToDto(ContentBlock block) => block switch
    {
        HeadingBlock heading => new(block.Kind, block.Position, heading.Text, null, null, null, null, null),
        TextBlock text => new(block.Kind, block.Position, text.Text, null, null, null, null, null),
        LinkBlock link => new(block.Kind, block.Position, null, link.Label, link.Url.AbsoluteUri, null, null, null),
        ImageBlock image => new(block.Kind, block.Position, null, null, image.Url.AbsoluteUri, image.AltText, image.Caption, null),
        GalleryBlock gallery => new(
            block.Kind,
            block.Position,
            null,
            null,
            null,
            null,
            null,
            gallery.Images.Select(image => new ItemDto(null, null, image.Url.AbsoluteUri, image.AltText, image.Caption)).ToArray()),
        KeyValueBlock keyValue => new(
            block.Kind,
            block.Position,
            null,
            null,
            null,
            null,
            null,
            keyValue.Items.Select(item => new ItemDto(item.Key, item.Value, null, null, null)).ToArray()),
        CodeBlock code => new(block.Kind, block.Position, code.Code, code.Language, null, null, null, null),
        _ => throw new InvalidOperationException($"Unsupported content block {block.GetType().Name}."),
    };

    private static ContentBlock FromDto(BlockDto block) => block.Kind switch
    {
        "heading" => new HeadingBlock(Required(block.Text), block.Position),
        "text" => new TextBlock(Required(block.Text), block.Position),
        "link" => new LinkBlock(Required(block.Label), new Uri(Required(block.Url)), block.Position),
        "image" => new ImageBlock(new Uri(Required(block.Url)), Required(block.Alt), block.Position, block.Caption),
        "gallery" => new GalleryBlock(
            Required(block.Items).Select(item =>
                new GalleryImage(new Uri(Required(item.Url)), Required(item.Alt), item.Caption)),
            block.Position),
        "key-value" => new KeyValueBlock(
            Required(block.Items).Select(item => new KeyValueItem(Required(item.Key), Required(item.Value))),
            block.Position),
        "code" => new CodeBlock(Required(block.Text), block.Position, block.Label),
        _ => throw new InvalidOperationException($"Unknown content block kind '{block.Kind}'."),
    };

    private static T Required<T>(T? value) where T : class =>
        value ?? throw new InvalidOperationException("Stored content block is incomplete.");

    private sealed record BlockDto(
        string Kind,
        int Position,
        string? Text,
        string? Label,
        string? Url,
        string? Alt,
        string? Caption,
        IReadOnlyList<ItemDto>? Items);

    private sealed record ItemDto(string? Key, string? Value, string? Url, string? Alt, string? Caption);
}
