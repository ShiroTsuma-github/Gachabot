namespace GachaBot.Domain.Content;

public abstract record ContentBlock
{
    protected ContentBlock(int position)
    {
        if (position < 1)
        {
            throw new DomainValidationException("Block position must be greater than zero.");
        }

        Position = position;
    }

    public int Position { get; }

    public abstract string Kind { get; }
}

public sealed record HeadingBlock : ContentBlock
{
    public HeadingBlock(string text, int position)
        : base(position)
    {
        Text = ContentBlockValidation.Required(text, nameof(text), 256);
    }

    public override string Kind => "heading";

    public string Text { get; }
}

public sealed record TextBlock : ContentBlock
{
    public TextBlock(string text, int position)
        : base(position)
    {
        Text = ContentBlockValidation.Required(text, nameof(text), 4_000);
    }

    public override string Kind => "text";

    public string Text { get; }
}

public sealed record LinkBlock : ContentBlock
{
    public LinkBlock(string label, Uri url, int position)
        : base(position)
    {
        Label = ContentBlockValidation.Required(label, nameof(label), 256);
        Url = ContentBlockValidation.PublicHttpUri(url, nameof(url));
    }

    public override string Kind => "link";

    public string Label { get; }

    public Uri Url { get; }
}

public sealed record ImageBlock : ContentBlock
{
    public ImageBlock(Uri url, string altText, int position, string? caption = null)
        : base(position)
    {
        Url = ContentBlockValidation.PublicHttpUri(url, nameof(url));
        AltText = ContentBlockValidation.Required(altText, nameof(altText), 512);
        Caption = ContentBlockValidation.Optional(caption, nameof(caption), 512);
    }

    public override string Kind => "image";

    public Uri Url { get; }

    public string AltText { get; }

    public string? Caption { get; }
}

public sealed record GalleryBlock : ContentBlock
{
    public GalleryBlock(IEnumerable<GalleryImage> images, int position)
        : base(position)
    {
        ArgumentNullException.ThrowIfNull(images);
        Images = images.ToArray();
        if (Images is { Count: < 1 or > 10 })
        {
            throw new DomainValidationException("A gallery must contain between 1 and 10 images.");
        }
    }

    public override string Kind => "gallery";

    public IReadOnlyList<GalleryImage> Images { get; }
}

public sealed record GalleryImage
{
    public GalleryImage(Uri url, string altText, string? caption = null)
    {
        Url = ContentBlockValidation.PublicHttpUri(url, nameof(url));
        AltText = ContentBlockValidation.Required(altText, nameof(altText), 512);
        Caption = ContentBlockValidation.Optional(caption, nameof(caption), 512);
    }

    public Uri Url { get; }

    public string AltText { get; }

    public string? Caption { get; }
}

public sealed record KeyValueBlock : ContentBlock
{
    public KeyValueBlock(IEnumerable<KeyValueItem> items, int position)
        : base(position)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items.ToArray();
        if (Items is { Count: < 1 or > 25 })
        {
            throw new DomainValidationException("A key/value block must contain between 1 and 25 items.");
        }
    }

    public override string Kind => "key-value";

    public IReadOnlyList<KeyValueItem> Items { get; }
}

public sealed record KeyValueItem
{
    public KeyValueItem(string key, string value)
    {
        Key = ContentBlockValidation.Required(key, nameof(key), 256);
        Value = ContentBlockValidation.Required(value, nameof(value), 1_024);
    }

    public string Key { get; }

    public string Value { get; }
}

public sealed record CodeBlock : ContentBlock
{
    public CodeBlock(string code, int position, string? language = null)
        : base(position)
    {
        Code = ContentBlockValidation.Required(code, nameof(code), 2_000);
        Language = ContentBlockValidation.Optional(language, nameof(language), 32);
    }

    public override string Kind => "code";

    public string Code { get; }

    public string? Language { get; }
}

internal static class ContentBlockValidation
{
    internal static string Required(string value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{name} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new DomainValidationException($"{name} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    internal static string? Optional(string? value, string name, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maximumLength);

    internal static Uri PublicHttpUri(Uri value, string name)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.IsAbsoluteUri || (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainValidationException($"{name} must be an absolute HTTP or HTTPS URI.");
        }

        return value;
    }
}
