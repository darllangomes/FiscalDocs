using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiscalDocs.Infrastructure.Messaging;

internal static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
