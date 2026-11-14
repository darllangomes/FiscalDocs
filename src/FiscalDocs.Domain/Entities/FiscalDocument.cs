

using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;

namespace FiscalDocs.Domain.Entities;

public sealed class FiscalDocument
{
    public const int MaxNotesLength = 500;

    public Guid Id { get; private set; }
    public DocumentType Type { get; private set; }

    public AccessKey AccessKey { get; private set; } = null!;

    public string Number { get; private set; } = null!;
    public string Series { get; private set; } = null!;
    public DateTimeOffset IssueDate { get; private set; }
    public Cnpj IssuerCnpj { get; private set; } = null!;
    public string IssuerName { get; private set; } = null!;
    public string IssuerState { get; private set; } = null!;
    public string? RecipientDocument { get; private set; }
    public string? RecipientName { get; private set; }
    public string? RecipientState { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string XmlHash { get; private set; } = null!;
    public string RawXml { get; private set; } = null!;
    public DocumentStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private FiscalDocument() { }

    public static FiscalDocument Create(
            DocumentType type,
        AccessKey accessKey,
        string number,
        string series,
        DateTimeOffset issueDate,
        Cnpj issuerCnpj,
        string issuerName,
        string issuerState,
        string? recipientDocument,
        string? recipientName,
        string? recipientState,
        decimal totalAmount,
        string rawXml,
        DateTimeOffset now
    )
    {
        ArgumentNullException.ThrowIfNull(accessKey);
        ArgumentNullException.ThrowIfNull(issuerCnpj);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(series);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawXml);
        ArgumentOutOfRangeException.ThrowIfNegative(totalAmount);

        var expectedModel = type switch
        {
            DocumentType.NFe => "55",
            DocumentType.CTe => "57",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        if(accessKey.Model != expectedModel)
        {
            throw new ArgumentException("Access key model does not match the document type.", nameof(accessKey));
        }
        if(accessKey.IssuerCnpj != issuerCnpj)
        {
              throw new ArgumentException("Issuer CNPJ does not match the access key.", nameof(issuerCnpj));
        }
       
       var state = issuerState?.Trim() ?? string.Empty;
       if(state.Length != 2 || !state.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("Issuer state must be a two-letter code.", nameof(issuerState));
        }

         return new FiscalDocument
        {
            Id = Guid.NewGuid(),
            Type = type,
            AccessKey = accessKey,
            Number = number.Trim(),
            Series = series.Trim(),
            IssueDate = issueDate,
            IssuerCnpj = issuerCnpj,
            IssuerName = issuerName.Trim(),
            IssuerState = state.ToUpperInvariant(),
            RecipientDocument = recipientDocument,
            RecipientName = recipientName,
            RecipientState = recipientState,
            TotalAmount = totalAmount,
            RawXml = rawXml,
            XmlHash = ComputeHash(rawXml),
            Status = DocumentStatus.Received,
            Notes = null,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
    public void UpdateManagement(DocumentStatus status, string? notes, DateTimeOffset now)
    {
        if(IsDeleted)
        {
            throw new InvalidOperationException("Cannot update a deleted document.");
        }
        if(!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if(normalizedNotes is {Length: > MaxNotesLength})
        {
            throw new ArgumentException($"Notes cannot exceed {MaxNotesLength} characters.", nameof(notes));
        }

        Status = status;
        Notes = normalizedNotes;
        UpdatedAt = now;
    }

    public void MarkAsDeleted(DateTimeOffset now)
    {
        if(IsDeleted)
        {
            return;
        }
        IsDeleted = true;
        UpdatedAt = now;
    }

    private static string ComputeHash(string rawXml) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawXml))).ToLowerInvariant();
}