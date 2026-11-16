using System.Xml.Linq;

namespace FiscalDocs.Application.Parsing;

public interface IXmlDocumentLoader
{
    /// <exception cref="Exceptions.InvalidDocumentException">XML malformado ou não permitido.</exception>
    XDocument Load(string xml);
}
