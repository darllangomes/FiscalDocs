


using System.Xml;
using System.Xml.Linq;
using FiscalDocs.Application.Exceptions;

namespace FiscalDocs.Infrastructure.Xml;

public static class SafeXmlLoader
{
    public const long DefaultMaxCharacters = 5_000_000;

    public static XDocument Load(Stream stream, long maxCharacters = DefaultMaxCharacters)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxCharacters
        };

        try
        {
            using var reader = XmlReader.Create(stream,settings);
            return XDocument.Load(reader);
        }catch(XmlException ex)
        {
            throw new InvalidDocumentException("The XML document is invalid or not allowed.", ex);
        }
    }
}