using Microsoft.OpenApi.Any;
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
            var idSchema = SchemaTypeMap[idPropertyType]();

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
                    Responses = { ["200"] = new OpenApiResponse { Description = "Success" } },
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
            }
        }


        foreach (var entity in registry.Entities)
        {
            if (!swaggerDoc.Components.Schemas.ContainsKey(entity.EntityType.Name.ToLower()))
            {
                swaggerDoc.Components.Schemas.Add(entity.EntityType.Name.ToLower(), new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>()
                });

                foreach (var prop in entity.Properties)
                {
                    var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (type.IsEnum)
                    {
                        swaggerDoc.Components.Schemas.Last().Value.Properties.Add(prop.Name, new OpenApiSchema
                        {
                            Type = "integer",
                            Format = "int32",
                            Enum = Enum.GetNames(type).Select(name => (IOpenApiAny)new OpenApiInteger((int)Enum.Parse(type, name)))
                                .ToList()
                        });
                    }
                    else
                    {
                        swaggerDoc.Components.Schemas.Last().Value.Properties.Add(prop.Name, SchemaTypeMap[type]());
                    }
                }
            }
        }

        if (registry.Entities.Any(x => x.ExposePagedResult)
            && !swaggerDoc.Components.Schemas.ContainsKey("pagedresult"))
        {
            swaggerDoc.Components.Schemas.Add("pagedresult", new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>()
            });

            foreach (var prop in typeof(OtterApiPagedResult).GetProperties())
            {
                var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (!prop.PropertyType.IsGenericType)
                {
                    if (type.IsEnum)
                    {
                        swaggerDoc.Components.Schemas.Last().Value.Properties.Add(prop.Name, new OpenApiSchema
                        {
                            Type = "integer",
                            Format = "int32",
                            Enum = Enum.GetNames(type).Select(name => (IOpenApiAny)new OpenApiInteger((int)Enum.Parse(type, name)))
                                .ToList()
                        });
                    }
                    else
                    {
                        swaggerDoc.Components.Schemas.Last().Value.Properties.Add(prop.Name, SchemaTypeMap[type]());
                    }
                }
                else
                {
                    swaggerDoc.Components.Schemas.Last().Value.Properties.Add(prop.Name, new OpenApiSchema
                    {
                        Type = "array",
                        Items = new OpenApiSchema
                        {
                            Type = "object"
                        }
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
}