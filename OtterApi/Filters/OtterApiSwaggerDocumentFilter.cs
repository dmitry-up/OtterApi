using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using OtterApi.Configs;
using OtterApi.Enums;
using OtterApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OtterApi.Filters;

public class OtterApiSwaggerDocumentFilter(OtterApiRegistry registry) : IDocumentFilter
{
    private static readonly Dictionary<Type, Func<OpenApiSchema>> SchemaTypeMap = new()
    {
        [typeof(string)] = () => new OpenApiSchema
        {
            Type = "string",
            Format = "string"
        },
        [typeof(DateTime)] = () => new OpenApiSchema
        {
            Type = "string",
            Format = "date-time"
        },
        [typeof(DateTimeOffset)] = () => new OpenApiSchema
        {
            Type = "string",
            Format = "date-time"
        },
        [typeof(Guid)] = () => new OpenApiSchema
        {
            Type = "string",
            Format = "uuid"
        },
        [typeof(short)] = () => new OpenApiSchema
        {
            Type = "integer",
            Format = "int32"
        },
        [typeof(ushort)] = () => new OpenApiSchema
        {
            Type = "integer",
            Format = "int32"
        },
        [typeof(int)] = () => new OpenApiSchema
        {
            Type = "integer",
            Format = "int32"
        },
        [typeof(uint)] = () => new OpenApiSchema
        {
            Type = "integer",
            Format = "int32"
        },
        [typeof(long)] = () => new OpenApiSchema
        {
            Type = "integer",
            Format = "int64"
        },
        [typeof(ulong)] = () => new OpenApiSchema
        {
            Type = "integer",
            Format = "int64"
        },
        [typeof(float)] = () => new OpenApiSchema
        {
            Type = "number",
            Format = "float"
        },
        [typeof(double)] = () => new OpenApiSchema
        {
            Type = "number",
            Format = "double"
        },
        [typeof(decimal)] = () => new OpenApiSchema
        {
            Type = "number",
            Format = "double"
        },
        [typeof(byte)] = () => new OpenApiSchema
        {
            Type = "integer",
            Format = "int32"
        },
        [typeof(sbyte)] = () => new OpenApiSchema
        {
            Type = "integer",
            Format = "int32"
        },
        [typeof(byte[])] = () => new OpenApiSchema
        {
            Type = "string",
            Format = "byte"
        },
        [typeof(sbyte[])] = () => new OpenApiSchema
        {
            Type = "string",
            Format = "byte"
        },
        [typeof(bool)] = () => new OpenApiSchema
        {
            Type = "boolean"
        }
    };

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach (var entity in registry.Entities)
        {
            var allowGet    = entity.AllowedOperations.HasFlag(OtterApiCrudOperation.Get);
            var allowPost   = entity.AllowedOperations.HasFlag(OtterApiCrudOperation.Post);
            var allowPut    = entity.AllowedOperations.HasFlag(OtterApiCrudOperation.Put);
            var allowDelete = entity.AllowedOperations.HasFlag(OtterApiCrudOperation.Delete);
            var allowPatch  = entity.AllowedOperations.HasFlag(OtterApiCrudOperation.Patch);

            // main route: GET list + POST
            var mainOperations = new Dictionary<OperationType, OpenApiOperation>();

            if (allowGet)
            {
                mainOperations[OperationType.Get] = new OpenApiOperation
                {
                    OperationId = GetOperationId($"{entity.Route.ToLower()}/get"),
                    Tags = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                    Description = $"Get all/filter items for {entity.EntityType.Name}",
                    Parameters = BuildGetListParameters(entity),
                    Responses =
                    {
                        ["200"] = new OpenApiResponse
                        {
                            Description = "Success",
                            Content =
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Type = "array",
                                        Items = new OpenApiSchema
                                        {
                                            Reference = new OpenApiReference
                                            {
                                                Type = ReferenceType.Schema,
                                                Id = $"{entity.EntityType.Name.ToLower()}"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };
            }

            if (entity.Id != null && allowPost)
            {
                mainOperations[OperationType.Post] = new OpenApiOperation
                {
                    OperationId = GetOperationId($"{entity.Route.ToLower()}/post"),
                    Tags = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                    Description = $"Create {entity.EntityType.Name}",
                    Responses =
                    {
                        ["201"] = new OpenApiResponse
                        {
                            Description = "Success",
                            Content =
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Reference = new OpenApiReference
                                        {
                                            Type = ReferenceType.Schema,
                                            Id = $"{entity.EntityType.Name.ToLower()}"
                                        }
                                    }
                                }
                            }
                        }
                    },
                    RequestBody = new OpenApiRequestBody
                    {
                        Content =
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Reference = new OpenApiReference
                                    {
                                        Type = ReferenceType.Schema,
                                        Id = $"{entity.EntityType.Name.ToLower()}"
                                    }
                                }
                            }
                        }
                    }
                };
            }

            if (mainOperations.Count > 0)
            {
                swaggerDoc.Paths.Add($"{entity.Route.ToLower()}", new OpenApiPathItem
                {
                    Operations = mainOperations
                });
            }

            if (entity.Id == null)
                continue;

            var idPropertyType = Nullable.GetUnderlyingType(entity.Id.PropertyType) ?? entity.Id.PropertyType;
            var idSchema = SchemaTypeMap.TryGetValue(idPropertyType, out var idSchemaFactory)
                ? idSchemaFactory()
                : new OpenApiSchema { Type = "string" };

            // {id} route: GET by id + PUT + DELETE
            var idOperations = new Dictionary<OperationType, OpenApiOperation>();

            if (allowGet)
            {
                idOperations[OperationType.Get] = new OpenApiOperation
                {
                    OperationId = GetOperationId($"{entity.Route.ToLower()}/getById"),
                    Tags = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                    Description = $"Get {entity.EntityType.Name} by id",
                    Responses =
                    {
                        ["200"] = new OpenApiResponse
                        {
                            Description = "Success",
                            Content =
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Reference = new OpenApiReference
                                        {
                                            Type = ReferenceType.Schema,
                                            Id = $"{entity.EntityType.Name.ToLower()}"
                                        }
                                    }
                                }
                            }
                        }
                    },
                    Parameters = new List<OpenApiParameter>
                    {
                        new() { Name = "id", Schema = idSchema, Required = true, In = ParameterLocation.Path }
                    }
                };
            }

            if (allowDelete)
            {
                idOperations[OperationType.Delete] = new OpenApiOperation
                {
                    OperationId = GetOperationId($"{entity.Route.ToLower()}/deleteById"),
                    Tags = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                    Description = $"Delete {entity.EntityType.Name} by id",
                    Responses =
                    {
                        ["204"] = new OpenApiResponse { Description = "No Content" },
                        ["404"] = new OpenApiResponse { Description = "Not Found" }
                    },
                    Parameters = new List<OpenApiParameter>
                    {
                        new() { Name = "id", Schema = idSchema, Required = true, In = ParameterLocation.Path }
                    }
                };
            }

            if (allowPut)
            {
                // ...existing PUT operation...
                idOperations[OperationType.Put] = new OpenApiOperation
                {
                    OperationId = GetOperationId($"{entity.Route.ToLower()}/updateById"),
                    Tags = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                    Description = $"Update {entity.EntityType.Name} by id",
                    Responses =
                    {
                        ["200"] = new OpenApiResponse
                        {
                            Description = "Success",
                            Content =
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Reference = new OpenApiReference
                                        {
                                            Type = ReferenceType.Schema,
                                            Id = $"{entity.EntityType.Name.ToLower()}"
                                        }
                                    }
                                }
                            }
                        }
                    },
                    Parameters = new List<OpenApiParameter>
                    {
                        new() { Name = "id", Schema = idSchema, Required = true, In = ParameterLocation.Path }
                    },
                    RequestBody = new OpenApiRequestBody
                    {
                        Content =
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Reference = new OpenApiReference
                                    {
                                        Type = ReferenceType.Schema,
                                        Id = $"{entity.EntityType.Name.ToLower()}"
                                    }
                                }
                            }
                        }
                    }
                };
            }

            if (allowPatch)
            {
                idOperations[OperationType.Patch] = new OpenApiOperation
                {
                    OperationId = GetOperationId($"{entity.Route.ToLower()}/patchById"),
                    Tags = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                    Description = $"Partially update {entity.EntityType.Name} by id (RFC 7396 JSON Merge Patch). " +
                                  "Only the fields present in the request body are updated; omitted fields are left unchanged.",
                    Responses =
                    {
                        ["200"] = new OpenApiResponse
                        {
                            Description = "Success",
                            Content =
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Reference = new OpenApiReference
                                        {
                                            Type = ReferenceType.Schema,
                                            Id = $"{entity.EntityType.Name.ToLower()}"
                                        }
                                    }
                                }
                            }
                        },
                        ["400"] = new OpenApiResponse { Description = "Validation error or missing Id" },
                        ["404"] = new OpenApiResponse { Description = "Record not found" }
                    },
                    Parameters = new List<OpenApiParameter>
                    {
                        new() { Name = "id", Schema = idSchema, Required = true, In = ParameterLocation.Path }
                    },
                    RequestBody = new OpenApiRequestBody
                    {
                        Description = "Partial entity — include only the fields to update.",
                        Content =
                        {
                            ["application/merge-patch+json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Reference = new OpenApiReference
                                    {
                                        Type = ReferenceType.Schema,
                                        Id = $"{entity.EntityType.Name.ToLower()}"
                                    }
                                }
                            }
                        }
                    }
                };
            }

            if (idOperations.Count > 0)
            {
                swaggerDoc.Paths.Add($"{entity.Route.ToLower()}/{{id}}", new OpenApiPathItem
                {
                    Operations = idOperations
                });
            }

            // count + pagedresult are GET-only sub-routes
            if (allowGet)
            {
                swaggerDoc.Paths.Add($"{entity.Route.ToLower()}/count", new OpenApiPathItem
                {
                    Operations =
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            OperationId = GetOperationId($"{entity.Route.ToLower()}/count"),
                            Tags = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                            Description = $"Get count of items for {entity.EntityType.Name}",
                            Parameters = BuildCountParameters(entity),
                            Responses =
                            {
                                ["200"] = new OpenApiResponse
                                {
                                    Description = "Success",
                                    Content =
                                    {
                                        ["application/json"] = new OpenApiMediaType
                                        {
                                            Schema = SchemaTypeMap[typeof(int)]()
                                        }
                                    }
                                }
                            }
                        }
                    }
                });

                if (entity.ExposePagedResult)
                {
                    swaggerDoc.Paths.Add($"{entity.Route.ToLower()}/pagedresult", new OpenApiPathItem
                    {
                        Operations =
                        {
                            [OperationType.Get] = new OpenApiOperation
                            {
                                OperationId = GetOperationId($"{entity.Route.ToLower()}/pagedresult"),
                                Tags = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                                Description = $"Get paged result of items for {entity.EntityType.Name}",
                                Parameters = BuildGetListParameters(entity),
                                Responses =
                                {
                                    ["200"] = new OpenApiResponse
                                    {
                                        Description = "Success",
                                        Content =
                                        {
                                            ["application/json"] = new OpenApiMediaType
                                            {
                                                Schema = new OpenApiSchema
                                                {
                                                    Reference = new OpenApiReference
                                                    {
                                                        Type = ReferenceType.Schema,
                                                        Id = "pagedresult"
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    });
                }

                // ── Custom named routes ────────────────────────────────────────────
                foreach (var cr in entity.CustomRoutes)
                {
                    var descParts = new List<string>
                    {
                        $"Custom preset GET route for {entity.EntityType.Name}."
                    };
                    if (!string.IsNullOrWhiteSpace(cr.Sort))
                        descParts.Add($"Default sort: {cr.Sort}.");
                    if (cr.Take > 0)
                        descParts.Add($"Limited to {cr.Take} record(s).");
                    if (cr.Single)
                        descParts.Add("Returns a single object or 404 when no match is found.");
                    else
                        descParts.Add("Returns an array (may be empty).");

                    // Response schema: object when single=true, array otherwise
                    var responseSchema = cr.Single
                        ? new OpenApiSchema
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.Schema,
                                Id   = entity.EntityType.Name.ToLower()
                            }
                        }
                        : new OpenApiSchema
                        {
                            Type  = "array",
                            Items = new OpenApiSchema
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.Schema,
                                    Id   = entity.EntityType.Name.ToLower()
                                }
                            }
                        };

                    var responses = new OpenApiResponses
                    {
                        ["200"] = new OpenApiResponse
                        {
                            Description = "Success",
                            Content =
                            {
                                ["application/json"] = new OpenApiMediaType { Schema = responseSchema }
                            }
                        }
                    };

                    if (cr.Single)
                        responses["404"] = new OpenApiResponse { Description = "No matching record found" };

                    swaggerDoc.Paths.Add($"{entity.Route.ToLower()}/{cr.Slug}", new OpenApiPathItem
                    {
                        Operations =
                        {
                            [OperationType.Get] = new OpenApiOperation
                            {
                                OperationId = GetOperationId($"{entity.Route.ToLower()}/{cr.Slug}"),
                                Tags        = new List<OpenApiTag> { new() { Name = entity.EntityType.Name } },
                                Description = string.Join(" ", descParts),
                                Parameters  = BuildGetListParameters(entity),
                                Responses   = responses
                            }
                        }
                    });
                }
            }
        }


        foreach (var entity in registry.Entities)
        {
            var schemaKey = entity.EntityType.Name.ToLower();
            if (!swaggerDoc.Components.Schemas.ContainsKey(schemaKey))
            {
                var entitySchema = new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>()
                };
                swaggerDoc.Components.Schemas.Add(schemaKey, entitySchema);

                foreach (var prop in entity.Properties)
                {
                    var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (type.IsEnum)
                        entitySchema.Properties.Add(prop.Name, BuildEnumSchema(type));
                    else if (SchemaTypeMap.TryGetValue(type, out var schemaFactory))
                        entitySchema.Properties.Add(prop.Name, schemaFactory());
                    else
                        entitySchema.Properties.Add(prop.Name, new OpenApiSchema { Type = "object" });
                }
            }
        }

        if (registry.Entities.Any(x => x.ExposePagedResult)
            && !swaggerDoc.Components.Schemas.ContainsKey("pagedresult"))
        {
            var pagedSchema = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>()
            };
            swaggerDoc.Components.Schemas.Add("pagedresult", pagedSchema);

            foreach (var prop in typeof(OtterApiPagedResult).GetProperties())
            {
                var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (!prop.PropertyType.IsGenericType)
                {
                    if (type.IsEnum)
                        pagedSchema.Properties.Add(prop.Name, BuildEnumSchema(type));
                    else if (SchemaTypeMap.TryGetValue(type, out var schemaFactory))
                        pagedSchema.Properties.Add(prop.Name, schemaFactory());
                    else
                        pagedSchema.Properties.Add(prop.Name, new OpenApiSchema { Type = "object" });
                }
                else
                {
                    pagedSchema.Properties.Add(prop.Name, new OpenApiSchema
                    {
                        Type  = "array",
                        Items = new OpenApiSchema { Type = "object" }
                    });
                }
            }

        }

    }

    public static string GetOperationId(string path)
    {
        return string.Join("",
            path.Split("/", StringSplitOptions.RemoveEmptyEntries).Select(x => x.First().ToString().ToUpper() + x.Substring(1)));
    }

    /// <summary>
    /// Builds the full set of query parameters for a GET list / pagedresult / custom-route operation:
    /// per-property filter[], sort[], page, pagesize, include (when nav-props present), operator.
    /// </summary>
    private static List<OpenApiParameter> BuildGetListParameters(OtterApiEntity entity)
    {
        var parameters = new List<OpenApiParameter>();

        // ── filter[{Prop}] — one entry per filterable property ────────────────
        foreach (var prop in entity.Properties)
        {
            var supportedOps = OtterApiConfiguration.Operators
                .Where(op => prop.PropertyType.IsOperatorSupported(op.Name))
                .Select(op => op.Name)
                .ToList();

            if (supportedOps.Count == 0) continue;

            parameters.Add(new OpenApiParameter
            {
                Name     = $"filter[{prop.Name}]",
                In       = ParameterLocation.Query,
                Required = false,
                Schema   = new OpenApiSchema { Type = "string" },
                Description = $"Filter by {prop.Name} (default operator: eq). " +
                              $"Supported operators: {string.Join(", ", supportedOps)}. " +
                              $"For non-eq use filter[{prop.Name}][operator]=value"
            });
        }

        // ── sort[{Prop}] — one entry per sortable property ────────────────────
        foreach (var prop in entity.Properties)
        {
            parameters.Add(new OpenApiParameter
            {
                Name     = $"sort[{prop.Name}]",
                In       = ParameterLocation.Query,
                Required = false,
                Schema   = new OpenApiSchema
                {
                    Type = "string",
                    Enum = new List<IOpenApiAny> { new OpenApiString("asc"), new OpenApiString("desc") }
                },
                Description = $"Sort by {prop.Name}: asc | desc"
            });
        }

        // ── Pagination ────────────────────────────────────────────────────────
        parameters.Add(new OpenApiParameter
        {
            Name     = "page",
            In       = ParameterLocation.Query,
            Required = false,
            Schema   = new OpenApiSchema { Type = "integer", Format = "int32" },
            Description = "Page number (1-based). Use together with pagesize."
        });
        parameters.Add(new OpenApiParameter
        {
            Name     = "pagesize",
            In       = ParameterLocation.Query,
            Required = false,
            Schema   = new OpenApiSchema { Type = "integer", Format = "int32" },
            Description = "Number of items per page. Clamped to MaxPageSize when configured."
        });

        // ── include ───────────────────────────────────────────────────────────
        if (entity.NavigationProperties.Count > 0)
        {
            var navNames = string.Join(", ", entity.NavigationProperties.Select(p => p.Name));
            parameters.Add(new OpenApiParameter
            {
                Name     = "include",
                In       = ParameterLocation.Query,
                Required = false,
                Schema   = new OpenApiSchema { Type = "string" },
                Description = $"Comma-separated navigation properties to eagerly load. Available: {navNames}"
            });
        }

        // ── operator ──────────────────────────────────────────────────────────
        parameters.Add(new OpenApiParameter
        {
            Name     = "operator",
            In       = ParameterLocation.Query,
            Required = false,
            Schema   = new OpenApiSchema
            {
                Type = "string",
                Enum = new List<IOpenApiAny> { new OpenApiString("and"), new OpenApiString("or") }
            },
            Description = "Logical operator to combine filter conditions: 'and' (default) or 'or'. " +
                          "For per-group operators use operator[N]=or."
        });

        return parameters;
    }

    /// <summary>
    /// Builds a reduced set of query parameters for the <c>/count</c> endpoint:
    /// filter[] per property and operator — no sort, page, pagesize, or include.
    /// </summary>
    private static List<OpenApiParameter> BuildCountParameters(OtterApiEntity entity)
    {
        var parameters = new List<OpenApiParameter>();

        foreach (var prop in entity.Properties)
        {
            var supportedOps = OtterApiConfiguration.Operators
                .Where(op => prop.PropertyType.IsOperatorSupported(op.Name))
                .Select(op => op.Name)
                .ToList();

            if (supportedOps.Count == 0) continue;

            parameters.Add(new OpenApiParameter
            {
                Name     = $"filter[{prop.Name}]",
                In       = ParameterLocation.Query,
                Required = false,
                Schema   = new OpenApiSchema { Type = "string" },
                Description = $"Filter by {prop.Name} (default: eq). Operators: {string.Join(", ", supportedOps)}"
            });
        }

        parameters.Add(new OpenApiParameter
        {
            Name     = "operator",
            In       = ParameterLocation.Query,
            Required = false,
            Schema   = new OpenApiSchema
            {
                Type = "string",
                Enum = new List<IOpenApiAny> { new OpenApiString("and"), new OpenApiString("or") }
            },
            Description = "Logical operator for filter conditions: 'and' (default) or 'or'."
        });

        return parameters;
    }

    /// <summary>
    /// Builds an OpenAPI integer schema for an enum type.
    /// - type: integer / format: int32  — matches actual serialization (WriteNumberValue)
    /// - enum: [0, 1, 2, ...]           — allowed numeric values
    /// - description: "0 = Pending, …"  — human-readable mapping
    /// - x-enumNames: ["Pending", …]    — machine-readable names for code generators
    ///                                     (NSwag, OpenAPI Generator, Kiota, etc.)
    /// </summary>
    private static OpenApiSchema BuildEnumSchema(Type enumType)
    {
        var names  = Enum.GetNames(enumType);
        var values = names.Select(n => (int)Enum.Parse(enumType, n)).ToArray();

        var namesExtension = new OpenApiArray();
        foreach (var n in names)
            namesExtension.Add(new OpenApiString(n));

        return new OpenApiSchema
        {
            Type        = "integer",
            Format      = "int32",
            Enum        = values.Select(v => (IOpenApiAny)new OpenApiInteger(v)).ToList(),
            Description = string.Join(", ", values.Select((v, i) => $"{v} = {names[i]}")),
            Extensions  = new Dictionary<string, IOpenApiExtension>
            {
                ["x-enumNames"] = namesExtension
            }
        };
    }
}