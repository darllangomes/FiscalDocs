namespace FiscalDocs.Application.Documents;

/// <summary>O XML guardado de um documento e a chave de acesso, usada para nomear o arquivo.</summary>
public sealed record DocumentXml(string AccessKey, string Xml);
