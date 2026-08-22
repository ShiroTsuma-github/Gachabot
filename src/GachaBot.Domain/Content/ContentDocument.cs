using System.Security.Cryptography;
using System.Text.Json;

namespace GachaBot.Domain.Content;

public sealed class ContentDocument
{
    private ContentDocument(IReadOnlyList<ContentBlock> blocks, string hash)
    {
        Blocks = blocks;
        Hash = hash;
    }

    public IReadOnlyList<ContentBlock> Blocks { get; }

    public string Hash { get; }

    public static ContentDocument Create(IEnumerable<ContentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var ordered = blocks.OrderBy(block => block.Position).ToArray();
        if (ordered.Length == 0)
        {
            throw new DomainValidationException("A content document must have at least one block.");
        }

        if (ordered.Select(block => block.Position).Distinct().Count() != ordered.Length)
        {
            throw new DomainValidationException("Block positions must be unique.");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, ordered);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
        return new ContentDocument(ordered, hash);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, IReadOnlyList<ContentBlock> blocks)
    {
        writer.WriteStartArray();
        foreach (var block in blocks)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", block.Kind);
            writer.WriteNumber("position", block.Position);
            switch (block)
            {
                case HeadingBlock heading:
                    writer.WriteString("text", heading.Text);
                    break;
                case TextBlock text:
                    writer.WriteString("text", text.Text);
                    break;
                case LinkBlock link:
                    writer.WriteString("label", link.Label);
                    writer.WriteString("url", link.Url.AbsoluteUri);
                    break;
                case ImageBlock image:
                    writer.WriteString("url", image.Url.AbsoluteUri);
                    writer.WriteString("alt", image.AltText);
                    WriteOptional(writer, "caption", image.Caption);
                    break;
                case GalleryBlock gallery:
                    writer.WriteStartArray("images");
                    foreach (var image in gallery.Images)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("url", image.Url.AbsoluteUri);
                        writer.WriteString("alt", image.AltText);
                        WriteOptional(writer, "caption", image.Caption);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    break;
                case KeyValueBlock keyValue:
                    writer.WriteStartArray("items");
                    foreach (var item in keyValue.Items)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("key", item.Key);
                        writer.WriteString("value", item.Value);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    break;
                case CodeBlock code:
                    writer.WriteString("code", code.Code);
                    WriteOptional(writer, "language", code.Language);
                    break;
                default:
                    throw new DomainValidationException($"Unsupported content block: {block.GetType().Name}.");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
