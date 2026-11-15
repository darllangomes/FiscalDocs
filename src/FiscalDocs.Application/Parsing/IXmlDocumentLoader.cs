using System.Xml.Linq;

namespace FiscalDocs.Application.Parsing;

public interface IXmlDocumentLoader
{
    /// <exception cref="Exceptions.InvalidDocumentException">Malformed or disallowed XML.</exception>
    XDocument Load(string xml);
}
