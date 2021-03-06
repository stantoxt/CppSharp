﻿using System.Collections.Generic;
using System.Linq;
using CppSharp.AST;
using CppSharp.AST.Extensions;

namespace CppSharp.Passes
{
    public class ParamTypeToInterfacePass : TranslationUnitPass
    {
        public ParamTypeToInterfacePass()
        {
            VisitOptions.VisitClassBases = false;
            VisitOptions.VisitClassFields = false;
            VisitOptions.VisitEventParameters = false;
            VisitOptions.VisitFunctionParameters = false;
            VisitOptions.VisitFunctionReturnType = false;
            VisitOptions.VisitNamespaceEnums = false;
            VisitOptions.VisitNamespaceEvents = false;
            VisitOptions.VisitNamespaceTemplates = false;
            VisitOptions.VisitNamespaceTypedefs = false;
            VisitOptions.VisitNamespaceVariables = false;
            VisitOptions.VisitTemplateArguments = false;
        }

        public override bool VisitFunctionDecl(Function function)
        {
            if (!base.VisitFunctionDecl(function))
                return false;

            // parameters and returns from a specialised interface
            // must not be replaced if the templated interface uses a template parameter
            Function templateInterfaceFunction = GetTemplateInterfaceFunction(function);

            if ((!function.IsOperator || function.Parameters.Count > 1) &&
                (templateInterfaceFunction == null ||
                 !IsTemplateParameter(templateInterfaceFunction.OriginalReturnType)))
            {
                var originalReturnType = function.OriginalReturnType;
                ChangeToInterfaceType(ref originalReturnType);
                function.OriginalReturnType = originalReturnType;
            }

            if (function.OperatorKind != CXXOperatorKind.Conversion &&
                function.OperatorKind != CXXOperatorKind.ExplicitConversion)
            {
                IList<Parameter> parameters = function.Parameters.Where(
                    p => p.Kind != ParameterKind.OperatorParameter &&
                        p.Kind != ParameterKind.IndirectReturnType).ToList();

                var templateFunctionParameters = new List<Parameter>();
                if (templateInterfaceFunction != null)
                    templateFunctionParameters.AddRange(
                        templateInterfaceFunction.Parameters.Where(
                            p => p.Kind != ParameterKind.OperatorParameter &&
                                p.Kind != ParameterKind.IndirectReturnType));
                for (int i = 0; i < parameters.Count; i++)
                {
                    if (templateFunctionParameters.Any() &&
                        IsTemplateParameter(templateFunctionParameters[i].QualifiedType))
                        continue;
                    var qualifiedType = parameters[i].QualifiedType;
                    ChangeToInterfaceType(ref qualifiedType);
                    parameters[i].QualifiedType = qualifiedType;
                }
            }

            return true;
        }

        public override bool VisitProperty(Property property)
        {
            if (!base.VisitProperty(property))
                return false;

            var templateInterfaceProperty = GetTemplateInterfaceProperty(property);

            if (templateInterfaceProperty != null &&
                IsTemplateParameter(templateInterfaceProperty.QualifiedType))
                return false;

            var type = property.QualifiedType;
            ChangeToInterfaceType(ref type);
            property.QualifiedType = type;
            return true;
        }

        private static Function GetTemplateInterfaceFunction(Function function)
        {
            Function templateInterfaceFunction = null;
            Class @class = function.OriginalNamespace as Class;
            if (@class != null && @class.IsInterface)
                templateInterfaceFunction = @class.Methods.First(
                   m => m.OriginalFunction == function.OriginalFunction).InstantiatedFrom;
            return templateInterfaceFunction;
        }

        private static Property GetTemplateInterfaceProperty(Property property)
        {
            if (property.GetMethod != null &&
                property.GetMethod.SynthKind == FunctionSynthKind.InterfaceInstance)
                return null;

            Property templateInterfaceProperty = null;
            Class @class = property.OriginalNamespace as Class;
            if (@class != null && @class.IsInterface)
            {
                var specialization = @class as ClassTemplateSpecialization;
                if (specialization != null)
                {
                    Class template = specialization.TemplatedDecl.TemplatedClass;
                    templateInterfaceProperty = template.Properties.FirstOrDefault(
                        p => p.Name == property.Name);
                }
            }

            return templateInterfaceProperty;
        }

        private static bool IsTemplateParameter(QualifiedType type)
        {
            return (type.Type.Desugar().GetFinalPointee() ?? type.Type).Desugar() is TemplateParameterType;
        }

        private static void ChangeToInterfaceType(ref QualifiedType type)
        {
            var finalType = (type.Type.GetFinalPointee() ?? type.Type).Desugar();
            Class @class;
            if (!finalType.TryGetClass(out @class))
                return;

            Class @interface = @class.GetInterface();
            if (@interface == null)
                return;

            type.Type = (Type) type.Type.Clone();
            finalType = (type.Type.GetFinalPointee() ?? type.Type).Desugar();
            finalType.TryGetClass(out @class, @interface);
        }
    }
}
