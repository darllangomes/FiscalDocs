using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FiscalDocs.Api.Swagger;

/// <summary>
/// Completa o que os comentários XML não alcançam: o corpo do POST (lido direto do fluxo da requisição, então o
/// Swashbuckle não o enxerga), os headers If-Match e ETag, e o filtro antigo que não deve aparecer.
/// </summary>
public sealed class OperationDocumentationFilter : IOperationFilter
{
    private const string RetiredFilter = "issuerCnpj";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor action)
            return;

        switch (action.ActionName)
        {
            case "Ingest":
                operation.RequestBody = XmlBody();
                break;
            case "GetById":
                AddETag(operation);
                break;
            case "Patch":
                operation.Parameters.Add(IfMatch(required: true));
                AddETag(operation);
                break;
            case "Delete":
                operation.Parameters.Add(IfMatch(required: false));
                break;
            case "List":
                var retired = operation.Parameters.FirstOrDefault(
                    p => p.Name.Equals(RetiredFilter, StringComparison.OrdinalIgnoreCase));
                if (retired is not null)
                    operation.Parameters.Remove(retired);

                // O binder de query ignora a caixa, mas o Swagger mostraria "IssuerDocument": o contrato é camelCase.
                foreach (var parameter in operation.Parameters)
                    parameter.Name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name);
                break;
        }
    }

    private static OpenApiRequestBody XmlBody()
    {
        OpenApiMediaType Media() => new()
        {
            Schema = new OpenApiSchema { Type = "string" },
            Example = new OpenApiString(IngestExample.Xml)
        };

        return new OpenApiRequestBody
        {
            Required = true,
            Description = "O XML completo da NFe ou do CTe (o `nfeProc`/`cteProc` ou o `NFe`/`CTe` raiz), até 2 MiB. " +
                          "Os dados do exemplo são fictícios.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/xml"] = Media(),
                ["text/xml"] = Media()
            }
        };
    }

    private static OpenApiParameter IfMatch(bool required) => new()
    {
        Name = "If-Match",
        In = ParameterLocation.Header,
        Required = required,
        Description = required
            ? "O ETag lido em `GET /api/v1/documents/{id}`, entre aspas, por exemplo `\"748\"`. Obrigatório: sem ele a resposta é 428."
            : "Opcional. O ETag lido em `GET /api/v1/documents/{id}`, entre aspas, por exemplo `\"748\"`. " +
              "Se for enviado e não for o atual, a resposta é 412.",
        Schema = new OpenApiSchema { Type = "string" },
        Example = new OpenApiString("\"748\"")
    };

    private static void AddETag(OpenApiOperation operation)
    {
        if (!operation.Responses.TryGetValue("200", out var ok))
            return;

        ok.Headers["ETag"] = new OpenApiHeader
        {
            Description = "Versão atual do documento, entre aspas. Envie-a em If-Match no PATCH e no DELETE.",
            Schema = new OpenApiSchema { Type = "string" },
            Example = new OpenApiString("\"748\"")
        };
    }
}

/// <summary>
/// Descrição e exemplo de cada campo dos contratos JSON. Os contratos são records posicionais, e os comentários
/// XML de um record não carregam exemplos, então a tabela fica aqui, num lugar só.
/// </summary>
public sealed class ContractDocumentationFilter : ISchemaFilter
{
    private static readonly Dictionary<string, (string Description, IOpenApiAny Example)> Document = new()
    {
        ["id"] = ("Identificador do documento. Use-o nas rotas `/api/v1/documents/{id}`.", S("4e23f295-5e89-4aa6-9f00-5eef6b835817")),
        ["type"] = ("Tipo do documento: `NFe` ou `CTe`.", S("NFe")),
        ["status"] = ("Status de gestão: `Received` (recém-recebido), `Reviewed` ou `Archived`. Só o PATCH o altera.", S("Received")),
        ["accessKey"] = ("Chave de acesso de 44 posições. Identifica o documento e leva o CNPJ do emitente (ou o CPF, com zeros à esquerda).", S("26260811222333000181550010000010011123456782")),
        ["number"] = ("Número do documento (`nNF` na NFe, `nCT` no CTe).", S("1001")),
        ["series"] = ("Série do documento.", S("1")),
        ["issueDate"] = ("Data e hora de emissão, normalizadas para UTC.", S("2026-08-14T13:30:00+00:00")),
        ["issuerDocument"] = ("CNPJ do emitente, por inteiro, ou o CPF dele (produtor rural pessoa física) mascarado como `***.982.247-**`.", S("11222333000181")),
        ["issuerName"] = ("Nome ou razão social do emitente.", S("EMPRESA FICTICIA NUMERICA LTDA")),
        ["issuerState"] = ("UF do emitente, com duas letras.", S("PE")),
        ["recipientName"] = ("Nome do destinatário, se o XML o informa. O documento do destinatário não é devolvido.", S("CLIENTE FICTICIO DA SILVA")),
        ["recipientState"] = ("UF do destinatário, se o XML a informa.", S("SP")),
        ["totalAmount"] = ("Valor total do documento, em reais.", new OpenApiDouble(1500.00)),
        ["notes"] = ("Anotações de gestão, até 500 caracteres. Nulo enquanto ninguém escrever.", S("Conferida pelo financeiro")),
        ["createdAt"] = ("Quando o documento foi gravado, em UTC.", S("2026-11-16T10:00:00+00:00")),
        ["updatedAt"] = ("Última alteração (PATCH ou exclusão), em UTC.", S("2026-11-16T10:05:00+00:00")),
    };

    private static readonly Dictionary<string, Dictionary<string, (string Description, IOpenApiAny Example)>> Overrides = new()
    {
        ["DocumentIngestedResponse"] = new()
        {
            ["id"] = ("Id do documento: o novo, se foi criado, ou o que já existia, se o XML era um reenvio idêntico.", S("4e23f295-5e89-4aa6-9f00-5eef6b835817")),
        },
        ["PatchDocumentRequest"] = new()
        {
            ["status"] = ("Novo status: `Received`, `Reviewed` ou `Archived`. Omita o campo para não alterá-lo.", S("Reviewed")),
            ["notes"] = ("Novas anotações, até 500 caracteres. Envie `null` ou texto em branco para limpar; omita o campo para não alterá-las.", S("Conferida pelo financeiro")),
        },
    };

    private static OpenApiString S(string value) => new(value);

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var name = context.Type.Name;
        if (name is not ("DocumentResponse" or "DocumentSummaryResponse" or "DocumentIngestedResponse" or "PatchDocumentRequest"))
            return;

        Overrides.TryGetValue(name, out var overrides);

        foreach (var (property, current) in schema.Properties.ToList())
        {
            if (overrides is not null && overrides.TryGetValue(property, out var specific))
                schema.Properties[property] = Documented(current, specific);
            else if (Document.TryGetValue(property, out var common))
                schema.Properties[property] = Documented(current, common);
        }
    }

    // Um $ref (as enumerações) ignora os campos irmãos no OpenAPI 3.0, então ele vira um allOf que carrega a descrição.
    private static OpenApiSchema Documented(OpenApiSchema property, (string Description, IOpenApiAny Example) doc)
    {
        if (property.Reference is not null)
            return new OpenApiSchema { AllOf = [property], Description = doc.Description, Example = doc.Example };

        property.Description = doc.Description;
        property.Example = doc.Example;
        return property;
    }
}
