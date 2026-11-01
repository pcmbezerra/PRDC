using System.Text;

namespace PRDC;

internal sealed record GuacamoleConnectionReference
{
    public GuacamoleConnectionReference(
        Uri baseUri,
        string dataSource,
        string connectionIdentifier,
        string displayName,
        string protocol)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "A URL base do Guacamole deve ser uma URI absoluta iniciada por http:// ou https://.",
                nameof(baseUri));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        BaseUri = EnsureTrailingSlash(baseUri);
        DataSource = dataSource.Trim();
        ConnectionIdentifier = connectionIdentifier.Trim();
        DisplayName = displayName.Trim();
        Protocol = protocol.Trim();
    }

    public Uri BaseUri { get; }

    public string DataSource { get; }

    public string ConnectionIdentifier { get; }

    public string DisplayName { get; }

    public string Protocol { get; }

    public string EncodeClientIdentifier()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(DataSource);

        string raw = string.Create(
            ConnectionIdentifier.Length + 3 + DataSource.Length,
            (ConnectionIdentifier, DataSource),
            static (span, state) =>
            {
                int pos = 0;
                state.ConnectionIdentifier.AsSpan().CopyTo(span[pos..]);
                pos += state.ConnectionIdentifier.Length;
                span[pos++] = '\0';
                span[pos++] = 'c';
                span[pos++] = '\0';
                state.DataSource.AsSpan().CopyTo(span[pos..]);
            });

        byte[] bytes = Encoding.UTF8.GetBytes(raw);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public Uri BuildClientUri()
    {
        Uri normalizedBaseUri = EnsureTrailingSlash(BaseUri);
        string clientId = EncodeClientIdentifier();
        return new Uri(normalizedBaseUri, $"#/client/{clientId}");
    }

    internal static Uri EnsureTrailingSlash(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        string absoluteUri = uri.AbsoluteUri;
        if (absoluteUri.EndsWith('/'))
        {
            return uri;
        }

        return new Uri(absoluteUri + "/", UriKind.Absolute);
    }
}
