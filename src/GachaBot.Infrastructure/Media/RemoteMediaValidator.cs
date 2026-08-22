using System.Net;
using System.Net.Sockets;

namespace GachaBot.Infrastructure.Media;

internal static class RemoteMediaValidator
{
    private static readonly HashSet<string> AllowedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    internal static async Task ValidateUriAsync(
        Uri sourceUrl,
        IHostAddressResolver addressResolver,
        CancellationToken cancellationToken)
    {
        if (!sourceUrl.IsAbsoluteUri || sourceUrl.Scheme != Uri.UriSchemeHttps || sourceUrl.IsDefaultPort is false)
        {
            throw new InvalidOperationException("Media URL must use HTTPS on the default port.");
        }

        var addresses = IPAddress.TryParse(sourceUrl.Host, out var literal)
            ? [literal]
            : await addressResolver.ResolveAsync(sourceUrl.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(IsPrivateOrReserved))
        {
            throw new InvalidOperationException("Media URL resolves to a private or reserved network.");
        }
    }

    internal static string ResolveContentType(
        string? declaredContentType,
        ReadOnlySpan<byte> signature)
    {
        if (!string.IsNullOrWhiteSpace(declaredContentType) && AllowedMediaTypes.Contains(declaredContentType))
        {
            return declaredContentType;
        }

        if (signature.StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
        {
            return "image/jpeg";
        }

        if (signature.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            return "image/png";
        }

        if (signature.StartsWith("GIF87a"u8) || signature.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (signature.Length >= 12 &&
            signature[..4].SequenceEqual("RIFF"u8) &&
            signature[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        throw new UnsupportedMediaTypeException(declaredContentType ?? string.Empty);
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is 0 or 10 or 127 ||
                bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                bytes[0] == 169 && bytes[1] == 254 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2 ||
                bytes[0] == 192 && bytes[1] == 168 ||
                bytes[0] == 198 && bytes[1] is 18 or 19 ||
                bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
                bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
                bytes[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsPrivateOrReserved(address.MapToIPv4());
            }

            var bytes = address.GetAddressBytes();
            return address.Equals(IPAddress.IPv6None) ||
                address.IsIPv6LinkLocal ||
                address.IsIPv6Multicast ||
                address.IsIPv6SiteLocal ||
                bytes[0] is 0xfc or 0xfd ||
                bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;
        }

        return true;
    }
}
