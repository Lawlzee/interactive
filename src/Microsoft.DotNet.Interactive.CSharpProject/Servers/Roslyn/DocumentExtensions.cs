// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.DotNet.Interactive.CSharpProject.MLS.Project;
using Microsoft.DotNet.Interactive.CSharpProject.Tools;

namespace Microsoft.DotNet.Interactive.CSharpProject.Servers.Roslyn
{
    public static class DocumentExtensions
    {
        public static bool IsMatch(this Document doc, ProjectFileContent fileContent) => 
            doc.IsMatch(fileContent.Name);

        public static bool IsMatch(this Document d, SourceFile source) => 
            d.IsMatch(source.Name);

        public static bool IsMatch(this Document d, string sourceName) =>
            d.Name == sourceName || d.FilePath == sourceName || (!string.IsNullOrWhiteSpace(sourceName) && (new RelativeFilePath(sourceName).Value == new RelativeFilePath(d.Name).Value));
    }
}