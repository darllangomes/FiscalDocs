using System.Xml.Linq;

namespace FiscalDocs.Application.Parsing;

public interface IDocumentParser
{
    bool CanParse(XDocument document);
    ParsedDocument Parse(XDocument document);
}