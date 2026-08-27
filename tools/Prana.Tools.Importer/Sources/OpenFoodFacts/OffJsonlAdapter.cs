using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Prana.Tools.Importer.Sources.OpenFoodFacts;

/// <summary>
/// Reads the Open Food Facts JSONL export.
/// </summary>
/// <remarks>
/// This is the JSONL export rather than the smaller tab separated one, and that choice is not
/// about convenience.
///
/// The tab separated export publishes only normalised <c>*_100g</c> columns. It has no
/// <c>nutrition_data_per</c> field and no per-serving columns at all, so there is no way to tell
/// whether a value was declared per 100 g on the packet or derived by dividing a per-serving
/// panel by its serving size. Importing it would label every product as per 100 g, which is
/// exactly the silent conversion DATA_POLICY.md forbids, committed invisibly across the whole
/// catalogue at once.
///
/// The JSONL export carries the full product document, including the declared basis and the
/// serving. It is around 12 GB compressed, so it is never stored: the workflow pipes it straight
/// from the network through decompression into this reader, and nothing larger than one product
/// is ever in memory.
/// </remarks>
public sealed class OffJsonlAdapter(Func<Stream> openStream, DateOnly retrievedAt) : ISourceAdapter
{
    public string Id => "openfoodfacts";

    public string DisplayName => "Open Food Facts";

    /// <summary>
    /// Both licences, because they cover different things. ODbL governs the database as a
    /// structure, DbCL governs the individual facts inside it.
    /// </summary>
    public string Licence => "ODbL-1.0 (database), DbCL-1.0 (contents)";

    public string Attribution => "Data from Open Food Facts (https://world.openfoodfacts.org), "
        + "made available under the Open Database License (ODbL) v1.0.";

    public DateOnly RetrievedAt => retrievedAt;

    public async IAsyncEnumerable<ImportCandidate> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var mapper = new OffMapper(retrievedAt, Licence);

        await using var raw = openStream();
        await using var stream = await OpenDecompressedAsync(raw, cancellationToken);

        // Contributed text contains malformed byte sequences. Replacing them keeps one bad
        // product from ending a twelve gigabyte import.
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1 << 20);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line;

            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (InvalidDataException)
            {
                // A truncated or corrupt tail ends the stream rather than losing the run. What
                // was read up to here is still valid, and the report shows how much that was.
                yield break;
            }

            if (line is null)
            {
                yield break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            ImportCandidate candidate;

            try
            {
                using var document = JsonDocument.Parse(line);
                var row = OffRow.FromJson(document.RootElement);

                candidate = mapper.TryMap(row, out var product, out var reason)
                    ? ImportCandidate.Accepted(product!.BarcodePrinted, product)
                    : ImportCandidate.Dropped(row["code"] ?? "(no code)", reason!);
            }
            catch (JsonException)
            {
                candidate = ImportCandidate.Dropped("(unparseable)", "the source line is not valid JSON");
            }

            yield return candidate;
        }
    }

    /// <summary>Opens a file, or standard input when the path is a single dash.</summary>
    public static Func<Stream> Open(string path) =>
        path == "-"
            ? Console.OpenStandardInput
            : () => File.OpenRead(path);

    /// <summary>
    /// Decompresses the stream when it is gzipped, and passes it through when it is not.
    /// </summary>
    /// <remarks>
    /// Detected from the first two bytes rather than from a flag or a file extension. The
    /// export arrives compressed from the network, but a pipeline may well have decompressed it
    /// already, and a caller having to remember which is a caller who will eventually get it
    /// wrong. Two bytes of evidence beats a flag.
    /// </remarks>
    internal static async Task<Stream> OpenDecompressedAsync(Stream raw, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        var read = await raw.ReadAtLeastAsync(header, 2, throwOnEndOfStream: false, cancellationToken);
        var restored = new PrefixedStream(header.AsMemory(0, read), raw);

        var gzipped = read == 2 && header[0] == 0x1F && header[1] == 0x8B;

        return gzipped ? new GZipStream(restored, CompressionMode.Decompress) : restored;
    }

    /// <summary>
    /// A read-only stream that replays a few bytes already taken from another stream.
    /// </summary>
    /// <remarks>
    /// Standard input cannot be rewound, so the bytes read to identify the format have to be
    /// handed back rather than sought over.
    /// </remarks>
    private sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
    {
        private int _prefixPosition;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_prefixPosition < prefix.Length)
            {
                var available = Math.Min(buffer.Length, prefix.Length - _prefixPosition);
                prefix.Span.Slice(_prefixPosition, available).CopyTo(buffer);
                _prefixPosition += available;
                return available;
            }

            return inner.Read(buffer);
        }

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
