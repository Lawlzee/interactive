﻿// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using FluentAssertions;
using System.Reflection;
using System.Text.Json;
using Pocket.For.Xunit;
using Xunit;

namespace Microsoft.DotNet.Interactive.Formatting.Tests
{
    public sealed class FormatterTests : FormatterTestBase
    {
        public class Defaults : FormatterTestBase
        {
            [Fact]
            public void Default_formatter_for_Type_displays_generic_parameter_name_for_single_parameter_generic_type()
            {
                typeof(List<string>).ToDisplayString()
                                    .Should().Be("System.Collections.Generic.List<System.String>");
                new List<string>().GetType().ToDisplayString()
                                  .Should().Be("System.Collections.Generic.List<System.String>");
            }

            [Fact]
            public void Default_formatter_for_Type_displays_generic_parameter_name_for_multiple_parameter_generic_type()
            {
                typeof(Dictionary<string, IEnumerable<int>>).ToDisplayString()
                                                            .Should().Be(
                                                                "System.Collections.Generic.Dictionary<System.String,System.Collections.Generic.IEnumerable<System.Int32>>");
            }

            [Fact]
            public void Default_formatter_for_Type_displays_generic_parameter_names_for_open_generic_types()
            {
                typeof(IList<>).ToDisplayString()
                               .Should().Be("System.Collections.Generic.IList<T>");
                typeof(IDictionary<,>).ToDisplayString()
                                      .Should().Be("System.Collections.Generic.IDictionary<TKey,TValue>");
            }

            [Fact]
            public void Custom_formatter_for_Type_can_be_registered()
            {
                Formatter.Register<Type>(t => t.GUID.ToString());

                GetType().ToDisplayString()
                         .Should().Be(GetType().GUID.ToString());
            }

            [Fact]
            public void Default_formatter_for_null_Nullable_indicates_null()
            {
                int? nullable = null;

                var output = nullable.ToDisplayString();

                output.Should().Be(((object) null).ToDisplayString());
            }

            [Fact]
            public void Exceptions_always_get_properties_formatters()
            {
                var exception = new ReflectionTypeLoadException(
                    new[]
                    {
                        typeof(FileStyleUriParser),
                        typeof(AssemblyKeyFileAttribute)
                    },
                    new Exception[]
                    {
                        new DataMisalignedException()
                    });

                var message = exception.ToDisplayString();

                message.Should().Contain(nameof(DataMisalignedException.Data));
                message.Should().Contain(nameof(DataMisalignedException.HResult));
                message.Should().Contain(nameof(DataMisalignedException.StackTrace));
            }

            [Fact]
            public void Exception_Data_is_included_by_default()
            {
                var ex = new InvalidOperationException("oh noes!", new NullReferenceException());
                var key = "a very important int";
                ex.Data[key] = 123456;

                var msg = ex.ToDisplayString();

                msg.Should().Contain(key);
                msg.Should().Contain("123456");
            }

            [Fact]
            public void Exception_StackTrace_is_included_by_default()
            {
                string msg;
                var ex = new InvalidOperationException("oh noes!", new NullReferenceException());

                try
                {
                    throw ex;
                }
                catch (Exception thrownException)
                {
                    msg = thrownException.ToDisplayString();
                }

                msg.Should()
                   .Contain($"StackTrace:    at {typeof(FormatterTests)}.{nameof(Defaults)}.{MethodInfo.GetCurrentMethod().Name}");
            }

            [Fact]
            public void Exception_Type_is_included_by_default()
            {
                var ex = new InvalidOperationException("oh noes!", new NullReferenceException());

                var msg = ex.ToDisplayString();

                msg.Should().Contain("InvalidOperationException");
            }

            [Fact]
            public void Exception_Message_is_included_by_default()
            {
                var ex = new InvalidOperationException("oh noes!", new NullReferenceException());

                var msg = ex.ToDisplayString();

                msg.Should().Contain("oh noes!");
            }

            [Fact]
            public void Exception_InnerExceptions_are_included_by_default()
            {
                var ex = new InvalidOperationException("oh noes!", new NullReferenceException("oh my.", new DataException("oops!")));

                ex.ToDisplayString()
                  .Should()
                  .Contain("NullReferenceException");
                ex.ToDisplayString()
                  .Should()
                  .Contain("DataException");
            }

            [Fact]
            public void When_ResetToDefault_is_called_then_default_formatters_are_immediately_reregistered()
            {
                var widget = new Widget { Name = "hola!" };

                var defaultValue = widget.ToDisplayString();

                Formatter.Register<Widget>(e => "hello!");

                widget.ToDisplayString().Should().NotBe(defaultValue);

                Formatter.ResetToDefault();

                widget.ToDisplayString().Should().Be(defaultValue);
            }
        }

        public class Registration : FormatterTestBase
        {
            [Fact]
            public void Can_Register_formatter_for_type_string()
            {
                var value = "hola!";

                var defaultValue = value.ToDisplayString();

                Formatter.Register<string>(e => "hello!");

                value.ToDisplayString().Should().NotBe(defaultValue);

                Formatter.ResetToDefault();

                value.ToDisplayString().Should().Be(defaultValue);
            }


            [Fact]
            public void ToDisplayString_uses_actual_type_formatter_and_not_compiled_type()
            {
                Widget widget = new InheritedWidget();
                var widgetFormatterCalled = false;
                var inheritedWidgetFormatterCalled = false;

                Formatter.Register<Widget>(w =>
                {
                    widgetFormatterCalled = true;
                    return "";
                });
                Formatter.Register<InheritedWidget>(w =>
                {
                    inheritedWidgetFormatterCalled = true;
                    return "";
                });

                widget.ToDisplayString();

                widgetFormatterCalled.Should().BeFalse();
                inheritedWidgetFormatterCalled.Should().BeTrue();
            }

            [Theory]
            [InlineData("text/html", "<div class=\"dni-plaintext\"># { This is the &lt;input&gt; &quot;yes&quot;\t\b\n\r }</div>")]
            [InlineData("text/plain", "# { This is the <input> \"yes\"\t\b\n\r }")]
            [InlineData("application/json", "\"# { This is the <input> \\\"yes\\\"\\t\\b\\n\\r }\"")]
            public void When_input_is_a_string_with_unusual_characters_then_it_is_encoded_appropriately(string mimeType, string expected)
            {
                var input = "# { This is the <input> \"yes\"\t\b\n\r }";

                var result = input.ToDisplayString(mimeType);

                result.Should().Be(expected);

            }

            [Theory]
            [InlineData("text/plain", false)]
            [InlineData("text/plain", true)]
            [InlineData("text/html", false)]
            [InlineData("text/html", true)]
            public void Formatters_can_be_registered_for_concrete_types(string mimeType, bool useGenericRegisterMethod)
            {
                if (useGenericRegisterMethod)
                {
                    Formatter.Register<FileInfo>(
                        formatter: (filInfo, writer) => writer.Write("hello"),
                        mimeType);
                }
                else
                {
                    Formatter.Register(
                        type: typeof(FileInfo),
                        formatter: (filInfo, writer) => writer.Write("hello"),
                        mimeType);
                }

                new FileInfo(@"c:\temp\foo.txt").ToDisplayString(mimeType)
                                                .Should()
                                                .Be("hello");
            }

            [Theory]
            [InlineData("text/plain", false)]
            [InlineData("text/plain", true)]
            [InlineData("text/html", false)]
            [InlineData("text/html", true)]
            public void Formatters_can_be_registered_for_obj_type(string mimeType, bool useGenericRegisterMethod)
            {
                if (useGenericRegisterMethod)
                {
                    Formatter.Register<object>(
                        formatter: (filInfo, writer) => writer.Write("hello"),
                        mimeType);
                }
                else
                {
                    Formatter.Register(
                        type: typeof(object),
                        formatter: (filInfo, writer) => writer.Write("hello"),
                        mimeType);
                }

                new FileInfo(@"c:\temp\foo.txt").ToDisplayString(mimeType)
                                                .Should()
                                                .Be("hello");
            }

            [Theory]
            [InlineData("text/plain", false)]
            [InlineData("text/plain", true)]
            [InlineData("text/html", false)]
            [InlineData("text/html", true)]
            public void Formatters_choose_exact_type_amongst_user_defined_formatters(string mimeType, bool useGenericRegisterMethod)
            {
                if (useGenericRegisterMethod)
                {
                    Formatter.Register<FileInfo>(
                        formatter: (filInfo, writer) => writer.Write("hello"),
                        mimeType);
                    Formatter.Register<object>(
                        formatter: (filInfo, writer) => writer.Write("world"),
                        mimeType);
                }
                else
                {
                    Formatter.Register(
                        type: typeof(FileInfo),
                        formatter: (filInfo, writer) => writer.Write("hello"),
                        mimeType);
                    Formatter.Register(
                        type: typeof(object),
                        formatter: (filInfo, writer) => writer.Write("world"),
                        mimeType);
                }

                // The FileInfo formatter is chosen for FileInfo
                new FileInfo(@"c:\temp\foo.txt").ToDisplayString(mimeType)
                                                .Should()
                                                .Be("hello");

                // The object formatter is chosen for System.Object
                (new object()).ToDisplayString(mimeType)
                              .Should()
                              .Be("world");

                // The object formatter is chosen for DirectoryInfo (which is a FileSystemInfo but not a FileInfo).
                new DirectoryInfo(@"c:\temp").ToDisplayString(mimeType)
                                             .Should()
                                             .Be("world");

            }

            [Theory]
            [InlineData("text/plain", false)]
            [InlineData("text/plain", true)]
            [InlineData("text/html", false)]
            [InlineData("text/html", true)]
            public void Formatters_choose_most_specific_type_amongst_user_defined_formatters(string mimeType, bool useGenericRegisterMethod)
            {
                if (useGenericRegisterMethod)
                {
                    Formatter.Register<IComparable>(
                        formatter: (filInfo, writer) => writer.Write("hello"),
                        mimeType);
                    Formatter.Register<object>(
                        formatter: (filInfo, writer) => writer.Write("world"),
                        mimeType);
                }
                else
                {
                    Formatter.Register(
                        type: typeof(IComparable),
                        formatter: (filInfo, writer) => writer.Write("hello"),
                        mimeType);
                    Formatter.Register(
                        type: typeof(object),
                        formatter: (filInfo, writer) => writer.Write("world"),
                        mimeType);
                }

                // The IComparable formatter is chosen for System.Int32, which supports 'IComparable'
                (100).ToDisplayString(mimeType)
                     .Should()
                     .Be("hello");

                // The IComparable formatter is chosen for System.DateTime, which supports 'IComparable'
                DateTime.Now.ToDisplayString(mimeType)
                        .Should()
                        .Be("hello");

                // The object formatter is chosen for something not supporting 'IComparable'
                // Note System.Type doesn't support IComparable.
                typeof(int).ToDisplayString(mimeType)
                           .Should()
                           .Be("world");
            }

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            public void Formatters_choose_most_recently_registered_formatter_is_preferred(string mimeType)
            {
                Formatter.Register<IComparable>(
                    formatter: (filInfo, writer) => writer.Write("hello"),
                    mimeType);
                Formatter.Register<object>(
                    formatter: (filInfo, writer) => writer.Write("world"),
                    mimeType);

                // The first IComparable formatter is chosen for System.Int32, which supports 'IComparable'
                (100).ToDisplayString(mimeType)
                     .Should()
                     .Be("hello");

                Formatter.Register<IComparable>(
                    formatter: (filInfo, writer) => writer.Write("hello again!"),
                    mimeType);

                // Now the second IComparable formatter is chosen for System.Int32, which supports 'IComparable'
                (100).ToDisplayString(mimeType)
                     .Should()
                     .Be("hello again!");

                // The object formatter is chosen for something not supporting 'IComparable'
                // Note System.Type doesn't support IComparable.
                typeof(int).ToDisplayString(mimeType)
                           .Should()
                           .Be("world");
            }

            [Theory]
            [InlineData("text/plain", false)]
            [InlineData("text/plain", true)]
            [InlineData("text/html", false)]
            [InlineData("text/html", true)]
            public void Formatters_can_be_registered_on_demand_for_non_generic_interfaces(string mimeType, bool useGenericRegisterMethod)
            {
                if (useGenericRegisterMethod)
                {
                    Formatter.Register<IEnumerable>(
                        formatter: (obj, writer) =>
                        {
                            var i = 0;
                            foreach (var item in obj)
                            {
                                i++;
                            }

                            writer.Write(i);
                        }, mimeType);
                }
                else
                {
                    Formatter.Register(
                        type: typeof(IEnumerable),
                        formatter: (obj, writer) =>
                        {
                            var i = 0;
                            foreach (var item in (IEnumerable) obj)
                            {
                                i++;
                            }

                            writer.Write(i);
                        }, mimeType);
                }

                var list = new ArrayList { 1, 2, 3, 4, 5 };

                list.ToDisplayString(mimeType)
                    .Should()
                    .Be(list.Count.ToString());
            }

            [Theory]
            [InlineData("text/plain", false)]
            [InlineData("text/plain", true)]
            [InlineData("text/html", false)]
            [InlineData("text/html", true)]
            public void Formatters_can_be_registered_on_demand_for_abstract_classes(string mimeType, bool useGenericRegisterMethod)
            {
                if (useGenericRegisterMethod)
                {
                    Formatter.Register<AbstractClass>(
                        (obj, writer) => writer.Write(obj.GetType().Name.ToUpperInvariant()), mimeType);
                }
                else
                {
                    Formatter.Register(
                        type: typeof(AbstractClass),
                        formatter: (obj, writer) => writer.Write(obj.GetType().Name.ToUpperInvariant()), mimeType);
                }

                var instanceOf1 = new ConcreteClass1InheritingAbstractClass();

                instanceOf1.ToDisplayString(mimeType)
                           .Should()
                           .Be(instanceOf1.GetType().Name.ToUpperInvariant());

                var instanceOf2 = new ConcreteClass2InheritingAbstractClass();

                instanceOf2.ToDisplayString(mimeType)
                           .Should()
                           .Be(instanceOf2.GetType().Name.ToUpperInvariant());
            }

            public abstract class AbstractClass
            {
            };

            public class ConcreteClass1InheritingAbstractClass : AbstractClass
            {
            };

            public class ConcreteClass2InheritingAbstractClass : AbstractClass
            {
            };

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            public void Formatters_can_be_registered_on_demand_for_open_generic_classes(string mimeType)
            {
                Formatter.Register(
                    type: typeof(List<>),
                    formatter: (obj, writer) =>
                    {
                        var i = 0;
                        foreach (var item in (IEnumerable) obj)
                        {
                            i++;
                        }

                        writer.Write(i);
                    }, mimeType);

                var list = new List<int> { 1, 2, 3, 4, 5 };

                list.ToDisplayString(mimeType)
                    .Should()
                    .Be(list.Count.ToString());
            }

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            public void Formatters_can_be_registered_on_demand_for_open_generic_interfaces(string mimeType)
            {
                Formatter.Register(
                    type: typeof(IList<>),
                    formatter: (obj, writer) =>
                    {
                        var i = 0;
                        foreach (var _ in (IEnumerable) obj)
                        {
                            i++;
                        }

                        writer.Write(i);
                    }, mimeType);

                var list = new List<int> { 1, 2, 3, 4, 5 };

                list.ToDisplayString(mimeType)
                    .Should()
                    .Be(list.Count.ToString());
            }

            [Fact]
            public void ListExpansionLimit_can_be_specified_per_type()
            {
                Formatter<Dictionary<string, int>>.ListExpansionLimit = 1000;
                Formatter.ListExpansionLimit = 4;
                var dictionary = new Dictionary<string, int>
                {
                    { "zero", 0 },
                    { "two", 2 },
                    { "three", 3 },
                    { "four", 4 },
                    { "five", 5 },
                    { "six", 6 },
                    { "seven", 7 },
                    { "eight", 8 },
                    { "nine", 9 },
                    { "ninety-nine", 99 }
                };

                var output = dictionary.ToDisplayString();

                output.Should().Contain("zero");
                output.Should().Contain("0");
                output.Should().Contain("ninety-nine");
                output.Should().Contain("99");
            }

            [Fact]
            public void Formatting_can_be_chosen_based_on_mime_type()
            {
                Formatter.Register(
                    new PlainTextFormatter<DateTime>((time, c) => c.Writer.Write("plain")));
                Formatter.Register(
                    new HtmlFormatter<DateTime>((time, c) => c.Writer.Write("html")));

                var now = DateTime.Now;

                now.ToDisplayString(PlainTextFormatter.MimeType).Should().Be("plain");
                now.ToDisplayString(HtmlFormatter.MimeType).Should().Be("html");
            }

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            public void Formatter_sources_can_provide_lazy_registration_of_custom_formatters(string mimeType)
            {
                var obj = new TypeWithCustomFormatter();

                var formatted = obj.ToDisplayString(mimeType);

                formatted.Should().Be($"Hello from {nameof(TypeWithCustomFormatter)} using MIME type {mimeType}");
            }

            [TypeFormatterSource(typeof(CustomFormatterSource))]
            private class TypeWithCustomFormatter
            {
            }

            private class CustomFormatterSource : ITypeFormatterSource
            {
                public IEnumerable<ITypeFormatter> CreateTypeFormatters()
                {
                    return new ITypeFormatter[]
                    {
                        new PlainTextFormatter<TypeWithCustomFormatter>(_ => $"Hello from {nameof(TypeWithCustomFormatter)} using MIME type text/plain"),
                        new HtmlFormatter<TypeWithCustomFormatter>(_ => $"Hello from {nameof(TypeWithCustomFormatter)} using MIME type text/html")
                    };
                }
            }
        }

        [LogToPocketLogger(@"c:\temp\text.log")]
        public class MimeTypeSettings : FormatterTestBase
        {
            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            public void Default_mime_type_sets_mime_type_for_all_types(string mimeType)
            {
                Formatter.DefaultMimeType = mimeType;

                Formatter.GetPreferredMimeTypesFor(typeof(int)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(object)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(Type)).Should().BeEquivalentTo(mimeType);
            }

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            public void Type_specific_mime_type_preference_applies_to_derived_types(string mimeType)
            {
                Formatter.SetPreferredMimeTypesFor(typeof(object), mimeType);

                Formatter.GetPreferredMimeTypesFor(typeof(int)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(object)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(string)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(Type)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(JsonElement)).Should().BeEquivalentTo(mimeType);
            }

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            [InlineData("text/whacky")]
            public void Last_specified_type_specific_mime_type_preference_applies_to_non_specified_derived_types(string mimeType)
            {
                // the last one should win
                Formatter.SetPreferredMimeTypesFor(typeof(object), mimeType);
                Formatter.SetPreferredMimeTypesFor(typeof(object), "text/plain");
                Formatter.SetPreferredMimeTypesFor(typeof(object), mimeType);
                Formatter.SetPreferredMimeTypesFor(typeof(object), "text/html");
                Formatter.SetPreferredMimeTypesFor(typeof(object), mimeType);

                Formatter.GetPreferredMimeTypesFor(typeof(int)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(object)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(string)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(Type)).Should().BeEquivalentTo(mimeType);
                Formatter.GetPreferredMimeTypesFor(typeof(JsonElement)).Should().BeEquivalentTo(mimeType);
            }

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            [InlineData("text/whacky")]
            public void Formatters_can_override_default_preference_for_base_type(string mimeType)
            {
                Formatter.SetPreferredMimeTypesFor(typeof(object), "text/default");
                Formatter.SetPreferredMimeTypesFor(typeof(int), mimeType);

                Formatter.GetPreferredMimeTypesFor(typeof(int)).Should().BeEquivalentTo(mimeType);
            }

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            [InlineData("text/whacky")]
            public void Formatters_can_override_default_mime_type(string mimeType)
            {
                Formatter.DefaultMimeType = "text/default";
                Formatter.SetPreferredMimeTypesFor(typeof(int), mimeType);

                Formatter.GetPreferredMimeTypesFor(typeof(int)).Should().BeEquivalentTo(mimeType);
            }

            [Theory]
            [InlineData("text/plain")]
            [InlineData("text/html")]
            [InlineData("text/whacky")]
            public void Formatters_can_clear_default_preference_for_single_type(string mimeType)
            {
                Formatter.SetPreferredMimeTypesFor(typeof(int), mimeType);
                Formatter.ResetToDefault();

                Formatter.GetPreferredMimeTypesFor(typeof(int)).Should().BeEquivalentTo("text/html");
            }
        }
    }
}
