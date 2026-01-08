using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Generators.Common;
using Avalonia.Generators.Compiler;
using XamlX;
using XamlX.Ast;
using XamlX.TypeSystem;

namespace Avalonia.Generators.StaticBindingGenerator
{
   /// <summary>
   /// Resolves StaticBinding markup extensions from XAML AST.
   /// </summary>
   internal class XamlXStaticBindingResolver : IXamlAstVisitor
   {
      private readonly List<StaticBindingInfo> _bindings = new();
      private readonly Stack<IXamlType?> _dataTypeStack = new();
      private readonly Dictionary<XamlAstObjectNode, string> _controlNames = new();
      private string _filePath = string.Empty;

      /// <summary>
      /// Resolves all StaticBinding usages in the XAML document.
      /// </summary>
      public IReadOnlyList<StaticBindingInfo> ResolveStaticBindings(XamlDocument xaml, string filePath)
      {
         _bindings.Clear();
         _dataTypeStack.Clear();
         _controlNames.Clear();
         _filePath = filePath;

         // First pass: collect all x:Name mappings
         CollectControlNames(xaml.Root);

         // Second pass: find StaticBinding usages
         _dataTypeStack.Push(null); // Root level has no DataType by default
         xaml.Root.Visit(this);
         xaml.Root.VisitChildren(this);
         _dataTypeStack.Pop();

         return _bindings;
      }

      /// <summary>
      /// First pass: collect all controls with x:Name attributes.
      /// </summary>
      private void CollectControlNames(IXamlAstNode node)
      {
         if (node is XamlAstObjectNode objectNode)
         {
            var controlName = GetControlName(objectNode);
            if (!string.IsNullOrEmpty(controlName))
            {
               _controlNames[objectNode] = controlName;
            }

            foreach (var child in objectNode.Children)
            {
               CollectControlNames(child);
            }
         }
         else if (node is XamlAstXamlPropertyValueNode propertyNode)
         {
            foreach (var value in propertyNode.Values)
            {
               CollectControlNames(value);
            }
         }
      }

      IXamlAstNode IXamlAstVisitor.Visit(IXamlAstNode node)
      {
         if (node is not XamlAstObjectNode objectNode)
            return node;

         var clrType = objectNode.Type.GetClrType();
         if (!clrType.IsAvaloniaStyledElement())
            return node;

         // Check if this control has x:DataType directive
         var dataType = GetDataType(objectNode);
         var hasDataType = dataType != null;
         if (hasDataType)
         {
            _dataTypeStack.Push(dataType);
         }

         // Check if this control has x:Name (required for StaticBinding)
         if (_controlNames.TryGetValue(objectNode, out var controlName) && !string.IsNullOrEmpty(controlName))
         {
            // Look for property assignments with StaticBinding
            FindStaticBindings(objectNode, controlName, clrType);
         }

         return node;
      }

      void IXamlAstVisitor.Push(IXamlAstNode node)
      {
         // Push DataType context when entering a node
      }

      void IXamlAstVisitor.Pop()
      {
         // Pop DataType context when leaving a node with x:DataType
         if (_dataTypeStack.Count > 1)
         {
            // Check if we need to pop (we pushed when entering a node with x:DataType)
            // This is handled in VisitChildren completion
         }
      }

      /// <summary>
      /// Finds StaticBinding markup extensions on the given control.
      /// </summary>
      private void FindStaticBindings(XamlAstObjectNode objectNode, string controlName, IXamlType controlType)
      {
         foreach (var child in objectNode.Children)
         {
            if (child is not XamlAstXamlPropertyValueNode propertyValueNode)
               continue;

            if (propertyValueNode.Property is not XamlAstNamePropertyReference propertyRef)
               continue;

            // Check each value for StaticBinding markup extension
            foreach (var value in propertyValueNode.Values)
            {
               if (value is not XamlAstObjectNode bindingNode)
                  continue;

               var bindingType = bindingNode.Type.GetClrType();
               if (!IsStaticBindingExtension(bindingType))
                  continue;

               // Extract binding information
               var bindingInfo = ExtractBindingInfo(
                  bindingNode,
                  controlName,
                  controlType.GetFqn(),
                  propertyRef.Name,
                  null); // Property type resolution can be added later if needed

               if (bindingInfo != null)
               {
                  _bindings.Add(bindingInfo);
               }
            }
         }
      }

      /// <summary>
      /// Extracts binding metadata from a StaticBinding markup extension node.
      /// </summary>
      private StaticBindingInfo? ExtractBindingInfo(
         XamlAstObjectNode bindingNode,
         string controlName,
         string controlType,
         string targetProperty,
         string? targetPropertyType)
      {
         var bindingInfo = new StaticBindingInfo
         {
            ControlName = controlName,
            ControlType = controlType,
            TargetProperty = targetProperty,
            TargetPropertyType = targetPropertyType,
            SourceFilePath = _filePath,
            LineNumber = bindingNode.Line,
            ColumnNumber = bindingNode.Position
         };

         // Get current DataType from stack
         var currentDataType = _dataTypeStack.Count > 0 ? _dataTypeStack.Peek() : null;
         bindingInfo.DataType = currentDataType?.GetFqn();
         bindingInfo.HasDataTypeInAncestors = currentDataType != null;

         // Extract binding properties from the markup extension
         foreach (var child in bindingNode.Children)
         {
            if (child is not XamlAstXamlPropertyValueNode propValue)
               continue;

            if (propValue.Property is not XamlAstNamePropertyReference propRef)
               continue;

            var propName = propRef.Name;
            var propValueText = GetPropertyValue(propValue);

            switch (propName)
            {
               case "Path":
                  bindingInfo.Path = propValueText ?? string.Empty;
                  break;

               case "Mode":
                  bindingInfo.Mode = ParseBindingMode(propValueText);
                  break;

               case "Converter":
                  // Converter is usually a StaticResource reference
                  bindingInfo.ConverterResourceKey = ExtractConverterResourceKey(propValue);
                  break;

               case "ConverterParameter":
                  bindingInfo.HasConverterParameter = !string.IsNullOrEmpty(propValueText);
                  break;

               case "StringFormat":
                  bindingInfo.StringFormat = propValueText;
                  break;

               case "FallbackValue":
                  bindingInfo.FallbackValue = propValueText;
                  break;

               case "TargetNullValue":
                  bindingInfo.TargetNullValue = propValueText;
                  break;

               case "RelativeSource":
                  bindingInfo.HasRelativeSource = true;
                  break;

               case "ElementName":
                  bindingInfo.HasElementName = !string.IsNullOrEmpty(propValueText);
                  break;

               case "UpdateSourceTrigger":
                  bindingInfo.HasUpdateSourceTrigger = !string.IsNullOrEmpty(propValueText);
                  break;
            }
         }

         return bindingInfo;
      }

      /// <summary>
      /// Extracts the x:Name from a control.
      /// </summary>
      private string? GetControlName(XamlAstObjectNode objectNode)
      {
         foreach (var child in objectNode.Children)
         {
            if (child is XamlAstXamlPropertyValueNode propertyValueNode &&
                propertyValueNode.Property is XamlAstNamePropertyReference namedProperty &&
                namedProperty.Name == "Name" &&
                propertyValueNode.Values.Count > 0 &&
                propertyValueNode.Values[0] is XamlAstTextNode text)
            {
               return text.Text;
            }
         }

         return null;
      }

      /// <summary>
      /// Extracts x:DataType directive from a control.
      /// </summary>
      private IXamlType? GetDataType(XamlAstObjectNode objectNode)
      {
         var dataTypeDirective = objectNode.Children
            .OfType<XamlAstXmlDirective>()
            .FirstOrDefault(dir => dir.Name == "DataType" && dir.Namespace == XamlNamespaces.Xaml2006);

         if (dataTypeDirective == null)
            return null;

         // DataType can be specified as a type reference
         if (dataTypeDirective.Values.Count > 0)
         {
            if (dataTypeDirective.Values[0] is XamlAstTextNode textNode)
            {
               // Type name as string - would need type resolution
               // For now, skip as we need the type system to resolve it
               return null;
            }
            else if (dataTypeDirective.Values[0] is XamlAstClrTypeReference typeRef)
            {
               return typeRef.Type;
            }
         }

         return null;
      }

      /// <summary>
      /// Gets the text value of a property.
      /// </summary>
      private string? GetPropertyValue(XamlAstXamlPropertyValueNode propValue)
      {
         if (propValue.Values.Count == 0)
            return null;

         if (propValue.Values[0] is XamlAstTextNode textNode)
            return textNode.Text;

         return null;
      }

      /// <summary>
      /// Extracts the converter resource key from a Converter property.
      /// </summary>
      private string? ExtractConverterResourceKey(XamlAstXamlPropertyValueNode propValue)
      {
         if (propValue.Values.Count == 0)
            return null;

         // Converter is typically {StaticResource ConverterKey}
         if (propValue.Values[0] is XamlAstObjectNode resourceNode)
         {
            var resourceType = resourceNode.Type.GetClrType();
            if (resourceType.Name == "StaticResourceExtension" ||
                resourceType.FullName?.Contains("StaticResource") == true)
            {
               // Find the ResourceKey property
               foreach (var child in resourceNode.Children)
               {
                  if (child is XamlAstXamlPropertyValueNode keyProp &&
                      keyProp.Property is XamlAstNamePropertyReference keyPropRef &&
                      (keyPropRef.Name == "ResourceKey" || keyPropRef.Name == "Key") &&
                      keyProp.Values.Count > 0 &&
                      keyProp.Values[0] is XamlAstTextNode keyText)
                  {
                     return keyText.Text;
                  }
               }

               // Some versions use constructor argument
               var ctorArg = resourceNode.Arguments.FirstOrDefault();
               if (ctorArg is XamlAstTextNode ctorText)
               {
                  return ctorText.Text;
               }
            }
         }

         return null;
      }

      /// <summary>
      /// Parses binding mode from string.
      /// </summary>
      private StaticBindingMode ParseBindingMode(string? mode)
      {
         if (string.IsNullOrEmpty(mode))
            return StaticBindingMode.Default;

         return mode switch
         {
            "OneTime" => StaticBindingMode.OneTime,
            "OneWay" => StaticBindingMode.OneWay,
            "TwoWay" => StaticBindingMode.TwoWay,
            "OneWayToSource" => StaticBindingMode.OneWayToSource,
            _ => StaticBindingMode.Default
         };
      }

      /// <summary>
      /// Checks if a type is StaticBindingExtension.
      /// </summary>
      private bool IsStaticBindingExtension(IXamlType type)
      {
         return type.Name == "StaticBindingExtension" ||
                type.FullName == "Avalonia.Markup.Xaml.MarkupExtensions.StaticBindingExtension";
      }
   }
}
