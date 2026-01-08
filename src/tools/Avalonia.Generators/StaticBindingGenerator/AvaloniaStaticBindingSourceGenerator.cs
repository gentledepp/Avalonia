using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Generators.Common;
using Avalonia.Generators.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Avalonia.Generators.StaticBindingGenerator
{
    /// <summary>
    /// Source generator for StaticBinding markup extension.
    /// Generates OnDataContextChanged implementations with direct property assignments.
    /// </summary>
    [Generator]
    public class AvaloniaStaticBindingSourceGenerator : ISourceGenerator
    {
        private const string SourceItemGroupMetadata = "build_metadata.AdditionalFiles.SourceItemGroup";

        public void Initialize(GeneratorInitializationContext context)
        {
            // No initialization required
        }

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                // Check if StaticBinding generator is enabled
                if (!IsGeneratorEnabled(context))
                    return;

                // Get XAML files
                var xamlFiles = ResolveAdditionalFiles(context);

                foreach (var xamlFile in xamlFiles)
                {
                    if (context.CancellationToken.IsCancellationRequested)
                        break;

                    ProcessXamlFile(context, xamlFile);
                }
            }
            catch (OperationCanceledException)
            {
                // Build cancelled
            }
            catch (Exception ex)
            {
                // Report unhandled error
                var descriptor = new DiagnosticDescriptor(
                    "AVLN4999",
                    "StaticBinding Generator Error",
                    "An error occurred in the StaticBinding generator: {0}",
                    "Generator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true);

                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor,
                    Location.None,
                    ex.Message));
            }
        }

        private bool IsGeneratorEnabled(GeneratorExecutionContext context)
        {
            // Check if AvaloniaStaticBindingGeneratorEnabled is set
            context.AnalyzerConfigOptions.GlobalOptions.TryGetValue(
                "build_property.AvaloniaStaticBindingGeneratorEnabled",
                out var enabled);

            return enabled == "true" || enabled == "True";
        }

        private IEnumerable<AdditionalText> ResolveAdditionalFiles(GeneratorExecutionContext context)
        {
            return context
                .AdditionalFiles
                .Where(f => context.AnalyzerConfigOptions
                    .GetOptions(f)
                    .TryGetValue(SourceItemGroupMetadata, out var sourceItemGroup)
                    && sourceItemGroup == "AvaloniaXaml");
        }

        private void ProcessXamlFile(GeneratorExecutionContext context, AdditionalText xamlFile)
        {
            var sourceText = xamlFile.GetText(context.CancellationToken);
            if (sourceText == null)
                return;

            var xamlContent = sourceText.ToString();

            // Quick check if file contains StaticBinding
            if (!xamlContent.Contains("StaticBinding"))
                return;

            try
            {
                // Setup XamlX infrastructure
                var compilation = (CSharpCompilation)context.Compilation;
                var typeSystem = new RoslynTypeSystem(compilation);
                var compiler = MiniCompiler.CreateDefault(
                    typeSystem,
                    "Avalonia.Metadata.XmlnsDefinitionAttribute");

                // Resolve view (x:Class)
                var viewResolver = new XamlXViewResolver(typeSystem, compiler);
                var resolvedView = viewResolver.ResolveView(xamlContent);

                if (resolvedView == null)
                    return; // No x:Class found

                // Resolve StaticBinding usages
                var bindingResolver = new XamlXStaticBindingResolver();
                var bindings = bindingResolver.ResolveStaticBindings(
                    resolvedView.Xaml,
                    xamlFile.Path);

                if (!bindings.Any())
                    return; // No StaticBinding usage found

                // Validate bindings and report diagnostics
                foreach (var binding in bindings)
                {
                    var location = CreateLocation(xamlFile.Path, binding.LineNumber, binding.ColumnNumber);
                    ValidateStaticBinding(context, binding, location);
                }

                // Get configuration options
                var enableBatching = GetConfigBool(context, "AvaloniaStaticBindingBatchUpdates", true);
                var batchDelayMs = GetConfigInt(context, "AvaloniaStaticBindingBatchDelayMs", 10);

                // Get DataType from first binding (all should have same DataType for a view)
                var dataType = bindings.FirstOrDefault()?.DataType ?? "object";

                // Generate code using StaticBindingCodeGenerator
                var generator = new StaticBindingCodeGenerator();
                var code = generator.GenerateCode(
                    resolvedView.ClassName,
                    resolvedView.Namespace,
                    dataType,
                    bindings.ToArray(),
                    enableBatching,
                    batchDelayMs);

                // Add generated source to compilation
                var fileName = $"{resolvedView.Namespace}.{resolvedView.ClassName}.StaticBinding.g.cs";
                context.AddSource(fileName, SourceText.From(code, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                // Report error for this specific file
                var descriptor = new DiagnosticDescriptor(
                    "AVLN4998",
                    "StaticBinding Processing Error",
                    "Error processing {0}: {1}",
                    "Generator",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true);

                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor,
                    Location.None,
                    xamlFile.Path,
                    ex.Message));
            }
        }

        private Location CreateLocation(string filePath, int lineNumber, int columnNumber)
        {
            var linePosition = new LinePosition(Math.Max(0, lineNumber - 1), Math.Max(0, columnNumber));
            var linePositionSpan = new LinePositionSpan(linePosition, linePosition);
            return Location.Create(filePath, TextSpan.FromBounds(0, 0), linePositionSpan);
        }

        private bool GetConfigBool(GeneratorExecutionContext context, string propertyName, bool defaultValue)
        {
            if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue(
                $"build_property.{propertyName}",
                out var value))
            {
                return value == "true" || value == "True";
            }

            return defaultValue;
        }

        private int GetConfigInt(GeneratorExecutionContext context, string propertyName, int defaultValue)
        {
            if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue(
                $"build_property.{propertyName}",
                out var value) &&
                int.TryParse(value, out var result))
            {
                return result;
            }

            return defaultValue;
        }

        /// <summary>
        /// Validates a StaticBinding and reports diagnostics for unsupported features.
        /// </summary>
        private void ValidateStaticBinding(
            GeneratorExecutionContext context,
            StaticBindingInfo binding,
            Location location)
        {
            // Check for unsupported features and emit diagnostics

            if (binding.HasConverterParameter)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StaticBindingDiagnostics.ConverterParameterNotSupported,
                    location));
            }

            if (binding.Mode == StaticBindingMode.TwoWay || binding.Mode == StaticBindingMode.OneWayToSource)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StaticBindingDiagnostics.InvalidBindingMode,
                    location,
                    binding.Mode.ToString()));
            }

            if (binding.HasRelativeSource)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StaticBindingDiagnostics.RelativeSourceNotSupported,
                    location));
            }

            if (binding.HasElementName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StaticBindingDiagnostics.ElementNameNotSupported,
                    location));
            }

            if (binding.HasUpdateSourceTrigger)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StaticBindingDiagnostics.UpdateSourceTriggerNotSupported,
                    location));
            }

            if (!binding.HasDataTypeInAncestors)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StaticBindingDiagnostics.MissingDataType,
                    location));
            }
        }
    }
}
