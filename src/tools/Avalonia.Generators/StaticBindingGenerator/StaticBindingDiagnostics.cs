using Microsoft.CodeAnalysis;

namespace Avalonia.Generators.StaticBindingGenerator
{
    /// <summary>
    /// Diagnostic descriptors for StaticBinding compile-time errors.
    /// </summary>
    internal static class StaticBindingDiagnostics
    {
        private const string Category = "Usage";

        public static readonly DiagnosticDescriptor MultiBindingNotSupported = new(
            id: "AVLN4001",
            title: "StaticBinding does not support MultiBinding",
            messageFormat: "StaticBinding cannot be used with MultiBinding. Use regular Binding instead.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "StaticBinding is designed for simple one-to-one bindings and does not support MultiBinding scenarios.");

        public static readonly DiagnosticDescriptor RelativeSourceNotSupported = new(
            id: "AVLN4002",
            title: "StaticBinding does not support RelativeSource",
            messageFormat: "StaticBinding does not support RelativeSource bindings. Use regular Binding instead.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "StaticBinding only supports bindings to the DataContext. For RelativeSource bindings, use regular Binding.");

        public static readonly DiagnosticDescriptor ConverterParameterNotSupported = new(
            id: "AVLN4003",
            title: "ConverterParameter not supported in StaticBinding MVP",
            messageFormat: "StaticBinding does not support ConverterParameter (MVP limitation). This will be added in a future release.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The current version of StaticBinding does not support ConverterParameter. This feature will be added in Phase 2.");

        public static readonly DiagnosticDescriptor InvalidBindingMode = new(
            id: "AVLN4004",
            title: "Invalid binding mode for StaticBinding",
            messageFormat: "StaticBinding only supports OneWay and OneTime modes. Found: {0}. Use regular Binding for TwoWay bindings.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "StaticBinding currently only supports OneWay and OneTime binding modes. For TwoWay or OneWayToSource bindings, use regular Binding.");

        public static readonly DiagnosticDescriptor ElementNameNotSupported = new(
            id: "AVLN4005",
            title: "StaticBinding does not support ElementName",
            messageFormat: "StaticBinding does not support ElementName bindings. Use regular Binding instead.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "StaticBinding only supports bindings to the DataContext. For ElementName bindings, use regular Binding.");

        public static readonly DiagnosticDescriptor MissingDataType = new(
            id: "AVLN4006",
            title: "StaticBinding requires x:DataType",
            messageFormat: "StaticBinding requires x:DataType to be set on the control or an ancestor element. Add x:DataType=\"YourViewModel\" to enable compile-time binding generation.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "StaticBinding generates code at compile time and requires x:DataType to know the data type. Set x:DataType on the control or a parent element.");

        public static readonly DiagnosticDescriptor UpdateSourceTriggerNotSupported = new(
            id: "AVLN4007",
            title: "StaticBinding does not support UpdateSourceTrigger",
            messageFormat: "StaticBinding does not support UpdateSourceTrigger. This property is only applicable to TwoWay bindings, which are not supported in the current version.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "UpdateSourceTrigger is only relevant for TwoWay bindings. Since StaticBinding only supports OneWay and OneTime modes, this property is not applicable.");
    }
}
