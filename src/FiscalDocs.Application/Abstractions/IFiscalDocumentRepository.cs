using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.ValueObjects;

namespace FiscalDocs.Application.Abstractions;

public interface IFiscalDocumentRepository
{
    /// <summary>Devolve false quando já existe um documento com a mesma chave de acesso.</summary>
    Task<bool> TryAddAsync(FiscalDocument document, CancellationToken cancellationToken);

    /// <summary>Busca pela chave de acesso, inclusive documentos apagados (soft delete).</summary>
    Task<FiscalDocument?> FindByAccessKeyAsync(AccessKey accessKey, CancellationToken cancellationToken);

    /// <summary>Modelo de leitura da API; exclui documentos apagados e nunca carrega o XML bruto.</summary>
    Task<DocumentDetails?> FindDetailsByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>O XML guardado e a chave de acesso; exclui documentos apagados. Só este método lê o XML bruto.</summary>
    Task<DocumentXml?> FindXmlByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Resumos filtrados e paginados, emissão mais recente primeiro; exclui documentos apagados.</summary>
    Task<PagedResult<DocumentSummary>> ListAsync(ListDocumentsQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Carrega o documento (apagados excluídos), executa <paramref name="apply"/> nele e salva,
    /// devolvendo PreconditionFailed quando a versão gravada difere de <paramref name="expectedVersion"/>.
    /// Exceções lançadas por <paramref name="apply"/> se propagam e nada é salvo.
    /// </summary>
    Task<UpdateDocumentResult> UpdateAsync(
        Guid id, uint expectedVersion, Action<FiscalDocument> apply, CancellationToken cancellationToken);

    /// <summary>Apaga o documento (soft delete); a versão só é conferida quando <paramref name="expectedVersion"/> é informada.</summary>
    Task<DeleteDocumentOutcome> DeleteAsync(
        Guid id, uint? expectedVersion, DateTimeOffset now, CancellationToken cancellationToken);
}
