﻿/*
 * Copyright 2020 James Courtney
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace FlatSharp.Compiler
{
    using System;
    using System.Collections.Generic;
    using Antlr4.Runtime.Misc;

    internal class FieldVisitor : FlatBuffersBaseVisitor<FieldDefinition>
    {
        private readonly FieldDefinition definition;

        public FieldVisitor()
        {
            this.definition = new FieldDefinition();
        }

        public override FieldDefinition VisitField_decl([NotNull] FlatBuffersParser.Field_declContext context)
        {
            this.definition.Name = context.IDENT().GetText();

            ErrorContext.Current.WithScope(this.definition.Name, () =>
            {
                Dictionary<string, string> metadata = new MetadataVisitor().VisitMetadata(context.metadata());
                this.SetFbsFieldType(context, metadata);

                string defaultValue = context.defaultValue_decl()?.GetText();
                if (defaultValue == "null")
                {
                    this.definition.IsOptionalScalar = true;
                }
                else if (!string.IsNullOrEmpty(defaultValue))
                {
                    this.definition.DefaultValue = defaultValue;
                }

                this.definition.Deprecated = metadata.ParseBooleanMetadata("deprecated");
                this.definition.IsKey = metadata.ParseBooleanMetadata("key");
                this.definition.NonVirtual = metadata.ParseNullableBooleanMetadata("nonVirtual");
                this.definition.SortedVector = metadata.ParseBooleanMetadata("sortedvector");
                this.definition.SharedString = metadata.ParseBooleanMetadata("sharedstring");

                this.ParseIdMetadata(metadata);
                this.definition.SetterKind = metadata.ParseMetadata(
                    "setter",
                    ParseSetterKind,
                    SetterKind.Public,
                    SetterKind.Public);

                // Attributes from FlatBuffers that we don't support.
                string[] unsupportedAttributes =
                {
                    "required", "force_align", "bit_flags", "flexbuffer", "hash", "original_order"
                };

                foreach (var unsupportedAttribute in unsupportedAttributes)
                {
                    if (metadata.ContainsKey(unsupportedAttribute))
                    {
                        ErrorContext.Current?.RegisterError($"FlatSharpCompiler does not support the '{unsupportedAttribute}' attribute in FBS files.");
                    }
                }
            });

            return this.definition;
        }

        private void ParseIdMetadata(IDictionary<string, string> metadata)
        {
            if (!metadata.TryParseIntegerMetadata("id", out int index))
            {
                if (index == MetadataHelpers.DefaultIntegerAttributeValueIfPresent)
                {
                    ErrorContext.Current?.RegisterError("Value of 'id' attribute should be set if attribute present.");
                }

                return;
            }

            if (index < 0)
            {
                ErrorContext.Current?.RegisterError($"Value of 'id' attribute {index} of '{definition.Name}' field is negative.");
            }

            definition.Index = index;
            definition.IsIndexSetManually = true;
        }

        private static bool ParseSetterKind(string value, out SetterKind setter)
        {
            return Enum.TryParse<SetterKind>(value, true, out setter);
        }

        private void SetFbsFieldType(FlatBuffersParser.Field_declContext context, Dictionary<string, string> metadata)
        {
            string fbsFieldType = context.type().GetText();

            this.definition.VectorType = VectorType.None;

            if (fbsFieldType.StartsWith("["))
            {
                this.definition.VectorType = VectorType.IList;

                // Trim the starting and ending square brackets.
                fbsFieldType = fbsFieldType.Substring(1, fbsFieldType.Length - 2);

                this.definition.VectorType = VectorType.IList;
                if (metadata.TryGetValue("vectortype", out string vectorTypeString))
                {
                    if (!Enum.TryParse<VectorType>(vectorTypeString, true, out var vectorType))
                    {
                        ErrorContext.Current?.RegisterError(
                            $"Unable to parse '{vectorTypeString}' as a vector type. Valid choices are: {string.Join(", ", Enum.GetNames(typeof(VectorType)))}.");
                    }

                    this.definition.VectorType = vectorType;
                }
            }
            else if (metadata.ContainsKey("vectortype"))
            {
                ErrorContext.Current?.RegisterError(
                    $"Non-vectors may not have the 'vectortype' attribute. Field = '{this.definition.Name}'");
            }

            this.definition.FbsFieldType = fbsFieldType;
        }
    }
}
