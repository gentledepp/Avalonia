using System;

namespace Avalonia.Generators.StaticBindingGenerator
{
    /// <summary>
    /// Binding modes supported by StaticBinding.
    /// </summary>
    internal enum StaticBindingMode
    {
        OneTime,
        OneWay,
        TwoWay,
        OneWayToSource,
        Default
    }

    /// <summary>
    /// Represents metadata about a StaticBinding extracted from XAML.
    /// </summary>
    internal class StaticBindingInfo
    {
        /// <summary>
        /// The property path (e.g., "Name", "Address.City").
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// The control name that has this binding (x:Name).
        /// </summary>
        public string ControlName { get; set; } = string.Empty;

        /// <summary>
        /// The type of the control (e.g., "TextBlock", "Button").
        /// </summary>
        public string ControlType { get; set; } = string.Empty;

        /// <summary>
        /// The property on the control being bound (e.g., "Text", "Command").
        /// </summary>
        public string TargetProperty { get; set; } = string.Empty;

        /// <summary>
        /// The type of the target property.
        /// </summary>
        public string? TargetPropertyType { get; set; }

        /// <summary>
        /// The binding mode (OneWay, OneTime).
        /// </summary>
        public StaticBindingMode Mode { get; set; } = StaticBindingMode.OneWay;

        /// <summary>
        /// The converter resource key, if any.
        /// </summary>
        public string? ConverterResourceKey { get; set; }

        /// <summary>
        /// Whether a converter is specified.
        /// </summary>
        public bool HasConverter => !string.IsNullOrEmpty(ConverterResourceKey);

        /// <summary>
        /// The string format, if any.
        /// </summary>
        public string? StringFormat { get; set; }

        /// <summary>
        /// The fallback value, if any.
        /// </summary>
        public string? FallbackValue { get; set; }

        /// <summary>
        /// The target null value, if any.
        /// </summary>
        public string? TargetNullValue { get; set; }

        /// <summary>
        /// Whether ConverterParameter is specified (not supported in MVP).
        /// </summary>
        public bool HasConverterParameter { get; set; }

        /// <summary>
        /// Whether RelativeSource is specified (not supported).
        /// </summary>
        public bool HasRelativeSource { get; set; }

        /// <summary>
        /// Whether ElementName is specified (not supported).
        /// </summary>
        public bool HasElementName { get; set; }

        /// <summary>
        /// Whether UpdateSourceTrigger is specified (not supported).
        /// </summary>
        public bool HasUpdateSourceTrigger { get; set; }

        /// <summary>
        /// Whether the control has x:DataType or an ancestor has it.
        /// </summary>
        public bool HasDataTypeInAncestors { get; set; } = true;

        /// <summary>
        /// The data type for the binding (from x:DataType).
        /// </summary>
        public string? DataType { get; set; }

        /// <summary>
        /// Source file path for diagnostics.
        /// </summary>
        public string? SourceFilePath { get; set; }

        /// <summary>
        /// Line number in source file.
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// Column number in source file.
        /// </summary>
        public int ColumnNumber { get; set; }
    }
}
