using System.Text.Json.Serialization;
using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FiscalDocs.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PatchDocumentRequest
{
    private string? _status;
    private string? _notes;

    public string? Status
    {
        get => _status;
        set
        {
            _status = value;
            StatusProvided = true;
        }
    }

    public string? Notes
    {
        get => _notes;
        set
        {
            _notes = value;
            NotesProvided = true;
        }
    }

    [JsonIgnore]
    public bool StatusProvided { get; private set; }

    [JsonIgnore]
    public bool NotesProvided { get; private set; }

    public UpdateDocumentCommand? ToCommand(Guid id, uint expectedVersion, ModelStateDictionary errors)
    {
        if (!StatusProvided && !NotesProvided)
        {
            errors.AddModelError(string.Empty, "At least one of status or notes must be provided.");
        }

        DocumentStatus? status = null;
        if (StatusProvided)
        {
            var name = Enum.GetNames<DocumentStatus>()
                .FirstOrDefault(n => n.Equals(Status, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                errors.AddModelError(
                    nameof(Status), $"status must be one of: {string.Join(", ", Enum.GetNames<DocumentStatus>())}.");
            }
            else
            {
                status = Enum.Parse<DocumentStatus>(name);
            }
        }

        return errors.IsValid
            ? new UpdateDocumentCommand(id, expectedVersion, status, NotesProvided, Notes)
            : null;
    }
}
