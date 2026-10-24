using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PharmacyDelivery.Api.Infrastructure;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PharmacyDelivery.Api.Configuration;

/// <summary>One Swagger document per API version, plus the JWT bearer security scheme.</summary>
public sealed class ConfigureSwaggerOptions(IApiVersionDescriptionProvider versions) : IConfigureOptions<SwaggerGenOptions>
{
    public const string BearerScheme = "Bearer";

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in versions.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, new OpenApiInfo
            {
                Title = "PharmacyDelivery q-commerce API",
                Version = $"{description.ApiVersion} (app {AppInfo.Version}, {AppInfo.Environment})",
                Description = "Orders of OTC pharmacy products with real-time courier tracking. "
                              + "Errors are RFC 9457 ProblemDetails with `code` and `traceId`. "
                              + (description.IsDeprecated ? "**This API version is deprecated.**" : string.Empty),
            });
        }

        options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token from POST /api/v1/auth/login.",
        });
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
        });

        var xml = Path.Combine(AppContext.BaseDirectory, typeof(ConfigureSwaggerOptions).Assembly.GetName().Name + ".xml");
        if (File.Exists(xml))
            options.IncludeXmlComments(xml, includeControllerXmlComments: true);

        options.SupportNonNullableReferenceTypes();
        options.CustomSchemaIds(type =>
            type.FullName?.Replace("PharmacyDelivery.Api.Contracts.", string.Empty, StringComparison.Ordinal).Replace('+', '.'));
    }
}
