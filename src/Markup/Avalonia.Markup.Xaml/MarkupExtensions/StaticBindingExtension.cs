using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Diagnostics;
using Avalonia.Markup.Parsers;

namespace Avalonia.Markup.Xaml.MarkupExtensions
{
    /// <summary>
    /// A markup extension that provides static bindings with code generation for optimal performance.
    /// Unlike regular bindings, StaticBinding generates direct property assignments in OnDataContextChanged
    /// eliminating the binding infrastructure overhead.
    /// </summary>
    /// <remarks>
    /// StaticBinding is designed for performance-critical scenarios like virtualized ListBox items.
    /// Supported features (MVP):
    /// - OneWay and OneTime binding modes
    /// - Value converters (without ConverterParameter)
    /// - StringFormat
    /// - FallbackValue and TargetNullValue
    /// - Batched PropertyChanged updates
    ///
    /// Unsupported features (will generate compile errors):
    /// - TwoWay and OneWayToSource modes
    /// - ConverterParameter
    /// - ElementName bindings
    /// - RelativeSource bindings
    /// - MultiBinding
    /// - UpdateSourceTrigger
    ///
    /// Requires x:DataType to be set on the control or an ancestor.
    /// </remarks>
    public class StaticBindingExtension : BindingBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StaticBindingExtension"/> class.
        /// </summary>
        public StaticBindingExtension()
        {
            // Default to OneWay mode for StaticBinding
            Mode = BindingMode.OneWay;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StaticBindingExtension"/> class.
        /// </summary>
        /// <param name="path">The binding path.</param>
        public StaticBindingExtension(string path)
            : this()
        {
            Path = path;
        }

        /// <summary>
        /// Gets or sets the property path for the binding.
        /// </summary>
        [ConstructorArgument("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the source object for the binding.
        /// Note: StaticBinding primarily works with DataContext. Source property support is limited.
        /// </summary>
        public object? Source { get; set; } = AvaloniaProperty.UnsetValue;

        /// <summary>
        /// Gets or sets the data type for compile-time binding.
        /// This is typically inferred from x:DataType on the parent element.
        /// </summary>
        public Type? DataType { get; set; }

        /// <summary>
        /// Provides the binding value.
        /// </summary>
        /// <param name="provider">Service provider for accessing XAML context.</param>
        /// <returns>A configured StaticBindingExtension instance.</returns>
        public StaticBindingExtension ProvideValue(IServiceProvider provider)
        {
            return new StaticBindingExtension
            {
                Path = Path,
                Converter = Converter,
                ConverterCulture = ConverterCulture,
                ConverterParameter = ConverterParameter,
                TargetNullValue = TargetNullValue,
                FallbackValue = FallbackValue,
                Mode = Mode,
                Priority = Priority,
                StringFormat = StringFormat,
                Source = Source,
                DataType = DataType,
                DefaultAnchor = new WeakReference(provider.GetDefaultAnchor()),
                UpdateSourceTrigger = UpdateSourceTrigger,
            };
        }

        /// <summary>
        /// Initiates a binding instance on the target object.
        /// </summary>
        [Obsolete(ObsoletionMessages.MayBeRemovedInAvalonia12)]
        public override InstancedBinding? Initiate(AvaloniaObject target, AvaloniaProperty? targetProperty, object? anchor = null,
            bool enableDataValidation = false)
        {
            // Fallback to regular binding if source generator hasn't run
            var fallbackBinding = CreateFallbackBinding();
            return fallbackBinding.Initiate(target, targetProperty, anchor, enableDataValidation);
        }

        /// <summary>
        /// Instantiates a binding expression for the target.
        /// </summary>
        /// <remarks>
        /// At runtime, StaticBinding acts as a marker. The actual binding behavior is generated
        /// by the StaticBinding source generator which creates OnDataContextChanged code.
        ///
        /// For runtime scenarios without source generation, this falls back to creating a regular
        /// reflection binding as a graceful degradation.
        /// </remarks>
        private protected override BindingExpressionBase Instance(
            AvaloniaObject target,
            AvaloniaProperty? targetProperty,
            object? anchor)
        {
            // Fallback to regular binding if source generator hasn't run
            // This ensures graceful degradation in runtime-only scenarios
            var enableDataValidation = targetProperty?.GetMetadata(target).EnableDataValidation ?? false;
            var fallbackBinding = CreateFallbackBinding();
            var instanced = fallbackBinding.Initiate(target, targetProperty, anchor, enableDataValidation);
            return instanced?.Expression ?? throw new InvalidOperationException("Failed to create binding expression");
        }

        /// <summary>
        /// Creates a fallback regular Binding for runtime scenarios without source generation.
        /// </summary>
        private Binding CreateFallbackBinding()
        {
            return new Binding
            {
                Path = Path,
                Converter = Converter,
                ConverterCulture = ConverterCulture,
                ConverterParameter = ConverterParameter,
                TargetNullValue = TargetNullValue,
                FallbackValue = FallbackValue,
                Mode = Mode,
                Priority = Priority,
                StringFormat = StringFormat,
                UpdateSourceTrigger = UpdateSourceTrigger,
                DefaultAnchor = DefaultAnchor,
                NameScope = NameScope,
                Source = Source,
            };
        }
    }
}
