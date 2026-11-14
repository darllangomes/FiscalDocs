using System.Xml.Linq;
using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Infrastructure.Xml;

public sealed class CTeParser : FiscalXmlParser
{
    private static readonly XNamespace CteNs = "http://www.portalfiscal.inf.br/cte";

    protected override XNamespace Ns => CteNs;
    protected override string[] RootNames { get; } = ["cteProc", "CTe"];
    protected override string InfoElementName => "infCte";
    protected override string IdPrefix => "CTe";
    protected override string NumberElementName => "nCT";
    protected override DocumentType Type => DocumentType.CTe;

    protected override string ReadTotalText(XElement inf) =>
        RequiredText(Required(inf, "vPrest"), "vTPrest");
}