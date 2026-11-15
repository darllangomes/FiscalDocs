using FiscalDocs.Application.Abstractions;

namespace FiscalDocs.Application.Documents;

public sealed class UpdateDocumentHandler(IFiscalDocumentRepository repository, TimeProvider clock)
{
    public async Task<UpdateDocumentResult> HandleAsync(
        UpdateDocumentCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        try
        {
            return await repository.UpdateAsync(
                command.Id,
                command.ExpectedVersion,
                document => document.UpdateManagement(
                    command.Status ?? document.Status,
                    command.NotesProvided ? command.Notes : document.Notes,
                    now),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return new UpdateDocumentResult(UpdateDocumentOutcome.Invalid, Error: ex.Message);
        }
    }
}
