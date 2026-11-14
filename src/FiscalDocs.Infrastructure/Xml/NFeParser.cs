using System.Xml.Linq;
using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Infrastructure.Xml;

public sealed class NFeParser : FiscalXmlParser
{
    private static readonly XNamespace NfeNs = "http://www.portalfiscal.inf.br/nfe";

    protected override XNamespace Ns => NfeNs;
    protected override string[] RootNames { get; } = ["nfeProc", "NFe"];
    protected override string InfoElementName => "infNFe";
    protected override string IdPrefix => "NFe";
    protected override string NumberElementName => "nNF";
    protected override DocumentType Type => DocumentType.NFe;

    protected override string ReadTotalText(XElement inf) =>
        RequiredText(Required(Required(inf, "total"), "ICMSTot"), "vNF");
}
