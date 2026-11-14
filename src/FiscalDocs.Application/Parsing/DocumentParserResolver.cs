using System.Xml.Linq;
using FiscalDocs.Application.Exceptions;
using FiscalDocs.Application.Parsing;

public sealed class DocumentParserResolver
{
    private readonly IReadOnlyList<IDocumentParser> _parsers;

    public DocumentParserResolver(IEnumerable<IDocumentParser> parsers) =>
    _parsers = parsers.ToArray();

    public IDocumentParser Resolve (XDocument document)
    {
        var matches = _parsers.Where(p => p.CanParse(document)).ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidDocumentException("Unsupported document type"),
            _ => throw new InvalidOperationException($"More than one parser accepts this document: {string.Join(", ", matches.Select(m => m.GetType().Name))}")
        };
    }
}