using System.Globalization;
using System.Xml.Linq;
using FiscalDocs.Application.Exceptions;
using FiscalDocs.Application.Parsing;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;

namespace FiscalDocs.Infrastructure.Xml;

public abstract class FiscalXmlParser : IDocumentParser
{
    protected abstract XNamespace Ns { get; }
    protected abstract string[] RootNames { get; }
    protected abstract string InfoElementName { get; }
    protected abstract string IdPrefix { get; }
    protected abstract string NumberElementName { get; }
    protected abstract DocumentType Type { get; }

    protected abstract string ReadTotalText(XElement inf);

    public bool CanParse(XDocument document) =>
        document.Root is { } root &&
        root.Name.Namespace == Ns &&
        RootNames.Contains(root.Name.LocalName);

    public ParsedDocument Parse(XDocument document)
    {
        var inf = document.Descendants(Ns + InfoElementName).FirstOrDefault()
                  ?? throw new InvalidDocumentException($"Element '{InfoElementName}' not found");
        var ide = Required(inf, "ide");
        var emit = Required(inf, "emit");

        var id = inf.Attribute("Id")?.Value;

        if (id is null ||
            !id.StartsWith(IdPrefix, StringComparison.Ordinal) ||
            !AccessKey.TryParse(id[IdPrefix.Length..], out var accessKey))
        {
            throw new InvalidDocumentException("Access key is missing or invalid.");
        }

        if (!Cnpj.TryParse(RequiredText(emit, "CNPJ"), out var issuerCnpj))
        {
            throw new InvalidDocumentException("Issuer CNPJ is invalid.");
        }

        if (accessKey.IssuerCnpj != issuerCnpj)
        {
            throw new InvalidDocumentException("Issuer CNPJ does not match the access key.");
        }

        var issuerName = RequiredText(emit, "xNome");
        var issuerState = RequiredText(Required(emit, "enderEmit"), "UF");
        var number = RequiredText(ide, NumberElementName);
        var series = RequiredText(ide, "serie");

        if (!DateTimeOffset.TryParse(RequiredText(ide, "dhEmi"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var issueDate))
        {
            throw new InvalidDocumentException("Issue date is invalid.");
        }

        if (!decimal.TryParse(ReadTotalText(inf), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var totalAmount))
        {
            throw new InvalidDocumentException("Total amount is invalid.");
        }

        string? recipientDocument = null;
        string? recipientName = null;
        string? recipientState = null;

        var dest = inf.Element(Ns + "dest");
        if (dest is not null)
        {
            recipientDocument = (dest.Element(Ns + "CPF")
                ?? dest.Element(Ns + "CNPJ")
                ?? dest.Element(Ns + "idEstrangeiro"))?.Value.Trim();
            recipientName = dest.Element(Ns + "xNome")?.Value.Trim();
            recipientState = dest.Element(Ns + "enderDest")?.Element(Ns + "UF")?.Value.Trim();
        }

        return new ParsedDocument(
            Type,
            accessKey,
            number,
            series,
            issueDate,
            issuerCnpj,
            issuerName,
            issuerState,
            recipientDocument,
            recipientName,
            recipientState,
            totalAmount);
    }

    protected XElement Required(XElement parent, string name) =>
        parent.Element(Ns + name)
        ?? throw new InvalidDocumentException($"Required element '{name}' not found.");

    protected string RequiredText(XElement parent, string name)
    {
        var value = Required(parent, name).Value.Trim();

        return value.Length > 0
            ? value
            : throw new InvalidDocumentException($"Required element '{name}' is empty.");
    }
}