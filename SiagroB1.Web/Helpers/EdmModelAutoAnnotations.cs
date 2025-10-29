using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Vocabularies;

namespace SiagroB1.Web.Helpers;

public static class EdmModelAutoAnnotations
{
    private const string UiNamespace = "UI";

    // Cache de termos para não duplicar no modelo
    private static readonly Dictionary<string, IEdmTerm> _termsCache = new();

    public static void ApplyAllAnnotations(EdmModel model, Assembly assembly, string schemaNamespace)
    {
        var entityTypes = assembly.GetTypes()
            .Where(t => t.IsClass && t.GetProperties().Any(p => p.GetCustomAttribute<KeyAttribute>() != null));

        foreach (var entityType in entityTypes)
        {
            var edmEntity = model.FindDeclaredType($"{schemaNamespace}.{entityType.Name}") as EdmEntityType;
            if (edmEntity == null) continue;

            foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var edmProperty = edmEntity.FindProperty(prop.Name);
                if (edmProperty == null) continue;

                ApplyAnnotationsToProperty(model, edmProperty, prop);
            }
        }
    }

    private static void ApplyAnnotationsToProperty(EdmModel model, IEdmProperty edmProperty, PropertyInfo prop)
    {
        // Label
        var displayAttr = prop.GetCustomAttribute<DisplayAttribute>();
        if (!string.IsNullOrWhiteSpace(displayAttr?.Name))
            AddAnnotation(model, edmProperty, "Label", new EdmStringConstant(displayAttr.Name));

        // Required
        if (prop.GetCustomAttribute<RequiredAttribute>() != null)
            AddAnnotation(model, edmProperty, "Required", new EdmBooleanConstant(true));

        // ReadOnly
        var readOnlyAttr = prop.GetCustomAttribute<ReadOnlyAttribute>();
        if (readOnlyAttr?.IsReadOnly == true)
            AddAnnotation(model, edmProperty, "ReadOnly", new EdmBooleanConstant(true));

        // MaxLength / MinLength
        var stringLength = prop.GetCustomAttribute<StringLengthAttribute>();
        var maxLength = prop.GetCustomAttribute<MaxLengthAttribute>();
        if (stringLength != null)
        {
            AddAnnotation(model, edmProperty, "MaxLength", new EdmIntegerConstant(stringLength.MaximumLength));
            if (stringLength.MinimumLength > 0)
                AddAnnotation(model, edmProperty, "MinLength", new EdmIntegerConstant(stringLength.MinimumLength));
        }
        else if (maxLength != null)
        {
            AddAnnotation(model, edmProperty, "MaxLength", new EdmIntegerConstant(maxLength.Length));
        }

        // Decimal precision / scale
        if (prop.PropertyType == typeof(decimal) || prop.PropertyType == typeof(double) || prop.PropertyType == typeof(float))
        {
            int precision = 18;
            int scale = 2;

            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            if (!string.IsNullOrEmpty(colAttr?.TypeName))
            {
                var parts = colAttr.TypeName.Replace("decimal", "").Replace("(", "").Replace(")", "").Split(',');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0], out precision);
                    int.TryParse(parts[1], out scale);
                }
            }

            AddAnnotation(model, edmProperty, "Precision", new EdmIntegerConstant(precision));
            AddAnnotation(model, edmProperty, "Scale", new EdmIntegerConstant(scale));

            var dataTypeAttr = prop.GetCustomAttribute<DataTypeAttribute>();
            if (dataTypeAttr?.DataType == DataType.Currency)
                AddAnnotation(model, edmProperty, "Currency", new EdmBooleanConstant(true));
        }
    }

    private static void AddAnnotation(EdmModel model, IEdmProperty property, string termName, IEdmExpression value)
    {
        var typeReference = value switch
        {
            EdmStringConstant _ => EdmCoreModel.Instance.GetString(false),
            EdmIntegerConstant _ => EdmCoreModel.Instance.GetInt32(false),
            EdmBooleanConstant _ => EdmCoreModel.Instance.GetBoolean(false),
            EdmDecimalConstant _ => EdmCoreModel.Instance.GetDecimal(false),
            _ => throw new NotSupportedException($"Tipo de annotation {value.GetType().Name} não suportado")
        };

        var term = GetOrCreateTerm(model, termName, typeReference);

        var annotation = new EdmVocabularyAnnotation(property, term, value);
        model.SetVocabularyAnnotation(annotation);
    }

    private static IEdmTerm GetOrCreateTerm(EdmModel model, string termName, IEdmTypeReference type)
    {
        if (_termsCache.TryGetValue(termName, out var term)) return term;

        term = new EdmTerm(UiNamespace, termName, type);
        model.AddElement(term);
        _termsCache[termName] = term;
        return term;
    }
}