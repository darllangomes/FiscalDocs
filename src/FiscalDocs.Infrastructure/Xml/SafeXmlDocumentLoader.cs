using System.Xml.Linq;
using FiscalDocs.Application.Parsing;

namespace FiscalDocs.Infrastructure.Xml;

public sealed class SafeXmlDocumentLoader : IXmlDocumentLoader
{
    public XDocument Load(string xml)
    {
        using var reader = new StringReader(xml);
        return SafeXmlLoader.Load(reader);
    }
}