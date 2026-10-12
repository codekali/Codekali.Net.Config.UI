using Codekali.Net.Config.UI.Models;

namespace Codekali.Net.Config.UI.Interfaces;

/// <summary>
/// Validates an appsettings JSON document against a JSON Schema (draft-07 subset).
/// </summary>
/// <remarks>
/// Supported keywords: <c>type</c>, <c>required</c>, <c>properties</c>,
/// <c>additionalProperties</c>, <c>minLength</c>, <c>maxLength</c>,
/// <c>pattern</c>, <c>minimum</c>, <c>maximum</c>, <c>enum</c>,
/// <c>minItems</c>, <c>maxItems</c>.
/// </remarks>
public interface ISchemaValidationService
{
    /// <summary>
    /// Returns true when a schema has been configured via
    /// <see cref="ConfigUIOptions.SchemaPath"/> or <see cref="ConfigUIOptions.SchemaJson"/>.
    /// </summary>
    bool IsSchemaConfigured { get; }

    /// <summary>
    /// Validates <paramref name="rawJson"/> against the configured schema.
    /// Returns an empty list when the document is valid or no schema is configured.
    /// </summary>
    Task<IReadOnlyList<SchemaViolation>> ValidateAsync(string rawJson, CancellationToken ct = default);

    /// <summary>
    /// Returns the raw JSON Schema string so the UI can pass it to Monaco Editor
    /// for live autocomplete and hover documentation.
    /// Returns <c>null</c> when no schema is configured.
    /// </summary>
    string? GetSchemaJson();
}