﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NJsonSchema;
using NJsonSchema.Generation;
using NJsonSchema.Validation;

namespace Moejoe.AspNet.JsonMergePatch.Internal
{
    internal class InternalValidator<TResource> where TResource : class
    {
        private readonly IContractResolver _contractResolver;

        public InternalValidator(IContractResolver contractResolver)
        {
            _contractResolver = contractResolver;
        }

        private Dictionary<string, string[]> CollectErrors(ICollection<ValidationError> result)
        {
            var errors = new Dictionary<string, string[]>();

            foreach (var error in result)
            {
                if (error.Kind == ValidationErrorKind.NullExpected) continue;
                if (error is ChildSchemaValidationError)
                {
                    var childError = error as ChildSchemaValidationError;
                    foreach (var child in CollectErrors(childError.Errors.Values.SelectMany(p => p).ToList()))
                        errors[child.Key] = child.Value
                            .Concat(errors.Keys.Contains(child.Key) ? errors[child.Key] : new string[] { }).ToArray();
                }
                else
                {
                    errors[error.Path] = (errors.Keys.Contains(error.Path) ? errors[error.Path] : new string[] { })
                        .Concat(new[] {error.Kind.ToString()}).ToArray();
                }
            }

            return errors;
        }

        private static void RemoveRequiredAttributes(JsonSchema4 schema)
        {
            schema.ActualSchema.RequiredProperties.Clear();
            foreach (var oneOf in schema.OneOf) RemoveRequiredAttributes(oneOf);
            foreach(var allOf in schema.AllOf) RemoveRequiredAttributes(allOf);
            foreach (var prop in schema.ActualProperties) RemoveRequiredAttributes(prop.Value);
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext, JObject target)
        {
            var task = Task.Run(async () => await JsonSchema4.FromTypeAsync<TResource>(new JsonSchemaGeneratorSettings
            {
                DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.Null,
                ContractResolver = _contractResolver
            }));
            var schema = task.Result;
            schema.AllowAdditionalProperties = true;
            RemoveRequiredAttributes(schema);
            var result = schema.Validate(target);
            if (!result.Any()) return new[] {ValidationResult.Success};
            var errors = CollectErrors(result.ToList());
            return errors.Select(p => new ValidationResult(string.Join(",", p.Value.Distinct()), new[] {p.Key}));
        }
    }
}