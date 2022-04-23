// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.CommandLine.Rendering;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive.Events;
using CompletionItem = System.CommandLine.Completions.CompletionItem;
using System.Reactive.Linq;
using NuGet.Configuration;
using NuGet.Common;
using NuGet.Protocol;
using System.Threading;
using NuGet.Protocol.Core.Types;
using System.Text;

namespace Microsoft.DotNet.Interactive
{
    public static class KernelSupportsNugetExtensions
    {
        public static T UseNugetDirective<T>(this T kernel)
            where T : Kernel, ISupportNuget
        {
            kernel.AddDirective(i());
            kernel.AddDirective(r());

            var restore = new Command("#!nuget-restore")
            {
                Handler = CommandHandler.Create(DoNugetRestore()),
                IsHidden = true
            };

            kernel.AddDirective(restore);

            return kernel;
        }

        private static readonly string installPackagesPropertyName = "commandIHandler.InstallPackages";

        private static Command i()
        {
            var iDirective = new Command("#i")
            {
                new Argument<string>("source")
            };
            iDirective.Handler = CommandHandler.Create<string, KernelInvocationContext>((source, context) =>
            {
                if (context.HandlingKernel is ISupportNuget kernel)
                {
                    kernel.TryAddRestoreSource(source.Replace("nuget:", ""));
                }
            });
            return iDirective;
        }

        private static Command r()
        {
            var settings = Settings.LoadDefaultSettings(Directory.GetCurrentDirectory());
            PackageSourceProvider provider = new PackageSourceProvider(settings);

            List<PackageSource> listEndpoints = provider.LoadPackageSources()
                .Where(p => p.IsEnabled)
                .ToList();

            SearchFilter searchFilter = new SearchFilter(includePrerelease: true);

            var searchTasks = new List<Task<IEnumerable<IPackageSearchMetadata>>>();


            List<PackageSearchResource> resources = new List<PackageSearchResource>();

            foreach (PackageSource source in listEndpoints)
            {
                SourceRepository repository = Repository.Factory.GetCoreV3(source);
                PackageSearchResource resource = repository.GetResourceAsync<PackageSearchResource>().Result;
                resources.Add(resource);
            }

            var packageArgument = new Argument<PackageReferenceOrFileInfo>("package", Parse);
            packageArgument.AddCompletions(GetCompletions);

            var rDirective = new Command("#r")
            {
                packageArgument
            };

            rDirective.Handler = CommandHandler.Create<PackageReferenceOrFileInfo, KernelInvocationContext>(HandleAddPackageReference);

            return rDirective;

            PackageReferenceOrFileInfo Parse(ArgumentResult result)
            {
                var token = result.Tokens
                    .Select(t => t.Value)
                    .SingleOrDefault();

                if (PackageReference.TryParse(token, out var reference))
                {
                    return reference;
                }

                if (token is not null &&
                    !token.StartsWith("nuget:") &&
                    !EndsInDirectorySeparator(token))
                {
                    return new FileInfo(token);
                }

                result.ErrorMessage = $"Unable to parse package reference: \"{token}\"";

                return null;

            }


            IEnumerable<CompletionItem> GetCompletions(CompletionContext context)
            {
                var token = context.ParseResult.Tokens[1].Value;

                if (PackageReference.TryParse(token, out var reference))
                {
                    var completions = GetNuGetCompletionsAsync(reference).Result;
                    return completions;
                }

                if ("nuget".StartsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    return new[]
                    {
                        new CompletionItem("nuget:")
                    };
                }

                return Array.Empty<CompletionItem>();
            }

            async Task<IEnumerable<CompletionItem>> GetNuGetCompletionsAsync(PackageReference packageReference)
            {
                SearchFilter searchFilter = new SearchFilter(includePrerelease: true);

                var searchTasks = new List<Task<IEnumerable<IPackageSearchMetadata>>>();

                ILogger logger = new NullLogger();
                CancellationToken cancellationToken = CancellationToken.None;

                foreach (PackageSearchResource resource in resources)
                {
                    searchTasks.Add(resource.SearchAsync(
                        packageReference.PackageName,
                        searchFilter,
                        skip: 0,
                        take: 100,
                        logger,
                        cancellationToken));
                }

                var searchResult = (await Task.WhenAll(searchTasks))
                    .SelectMany(x => x)
                    .Where(x => x.Identity.Id.StartsWith(packageReference.PackageName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var completions = new List<CompletionItem>();

                foreach (var package in searchResult)
                {
                    if (package.Identity.Id.Equals(packageReference.PackageName, StringComparison.OrdinalIgnoreCase))
                    {
                        completions.Add(CreateCompletionItem(package, package.DownloadCount, version: "*-*"));

                        var versions = (await package.GetVersionsAsync()).ToList();

                        string orderFormat = $"D{Math.Ceiling(Math.Log10(versions.Count))}";

                        var versionCompletions = versions
                            .Select((x, i) => CreateCompletionItem(
                                package,
                                x.DownloadCount,
                                x.Version.ToString(),
                                order: (versions.Count - i).ToString(orderFormat)));

                        completions.AddRange(versionCompletions);
                    }
                    else
                    {
                        completions.Add(CreateCompletionItem(package));
                    }
                }

                return completions;
            }

            CompletionItem CreateCompletionItem(IPackageSearchMetadata package, long? downloadCount = null, string version = null, string order = null)
            {
                var label = version is null
                    ? $"nuget:{package.Identity.Id}"
                    : $"nuget:{package.Identity.Id},{version}";

                string sortText = order is null
                    ? $"nuget:{package.Identity.Id}"
                    : $"nuget:{package.Identity.Id},{order}";

                StringBuilder documentation = new StringBuilder();
                documentation.AppendLine(package.Description);
                documentation.AppendLine();

                documentation.Append($"Version: ");
                documentation.AppendLine(version);

                documentation.Append($"Authors: ");
                documentation.AppendLine(package.Authors);

                documentation.Append($"Downloads: ");
                documentation.AppendLine((downloadCount ?? package.DownloadCount)?.ToString());

                documentation.Append($"Tags: ");
                documentation.AppendLine(package.Tags);

                return new CompletionItem(label, sortText: sortText, documentation: documentation.ToString());
            }

            Task HandleAddPackageReference(
                PackageReferenceOrFileInfo package,
                KernelInvocationContext context)
            {
                if (package?.Value is PackageReference pkg &&
                    context.HandlingKernel is ISupportNuget kernel)
                {
                    var alreadyGotten = kernel.ResolvedPackageReferences
                                              .Concat(kernel.RequestedPackageReferences)
                                              .FirstOrDefault(r => r.PackageName.Equals(pkg.PackageName, StringComparison.OrdinalIgnoreCase));

                    if (alreadyGotten is { } && !string.IsNullOrWhiteSpace(pkg.PackageVersion) && pkg.PackageVersion != alreadyGotten.PackageVersion)
                    {
                        if (!pkg.IsPackageVersionSpecified || pkg.PackageVersion is "*-*" or "*")
                        {
                            // we will reuse the the already loaded since this is a wildcard
                            var added = kernel.GetOrAddPackageReference(alreadyGotten.PackageName, alreadyGotten.PackageVersion);

                            if (added is null)
                            {
                                var errorMessage = GenerateErrorMessage(pkg).ToString(OutputMode.NonAnsi);
                                context.Fail(context.Command, message: errorMessage);
                            }
                        }
                        else
                        {
                            var errorMessage = GenerateErrorMessage(pkg, alreadyGotten).ToString(OutputMode.NonAnsi);
                            context.Fail(context.Command, message: errorMessage);
                        }
                    }
                    else
                    {
                        var added = kernel.GetOrAddPackageReference(pkg.PackageName, pkg.PackageVersion);

                        if (added is null)
                        {
                            var errorMessage = GenerateErrorMessage(pkg).ToString(OutputMode.NonAnsi);
                            context.Fail(context.Command, message: errorMessage);
                        }
                    }

                    static TextSpan GenerateErrorMessage(
                        PackageReference requested,
                        PackageReference existing = null)
                    {
                        var spanFormatter = new TextSpanFormatter();
                        if (existing is not null)
                        {
                            if (!string.IsNullOrEmpty(requested.PackageName))
                            {
                                if (!string.IsNullOrEmpty(requested.PackageVersion))
                                {
                                    return spanFormatter.ParseToSpan(
                                        $"{Ansi.Color.Foreground.Red}{requested.PackageName} version {requested.PackageVersion} cannot be added because version {existing.PackageVersion} was added previously.{Ansi.Text.AttributesOff}");
                                }
                            }
                        }

                        return spanFormatter.ParseToSpan($"Invalid Package specification: '{requested}'");
                    }
                }

                return Task.CompletedTask;
            }
        }

        private static bool EndsInDirectorySeparator(string path)
        {
            return path.Length > 0 && path.EndsWith(Path.DirectorySeparatorChar);
        }

        private static void CreateOrUpdateDisplayValue(KernelInvocationContext context, string name, object content)
        {
            if (!context.Command.Properties.TryGetValue(name, out var displayed))
            {
                displayed = context.Display(content);
                context.Command.Properties.Add(name, displayed);
            }
            else
            {
                (displayed as DisplayedValue)?.Update(content);
            }
        }

        internal static KernelCommandInvocation DoNugetRestore()
        {
            return async (_, invocationContext) =>
            {
                async Task Restore(KernelInvocationContext context)
                {
                    if (context.HandlingKernel is not ISupportNuget kernel)
                    {
                        return;
                    }

                    var requestedPackages = kernel.RequestedPackageReferences.Select(s => s.PackageName).OrderBy(s => s).ToList();

                    var requestedSources = kernel.RestoreSources.OrderBy(s => s).ToList();

                    var installMessage = new InstallPackagesMessage(requestedSources, requestedPackages, Array.Empty<string>(), 0);

                    CreateOrUpdateDisplayValue(context, installPackagesPropertyName, installMessage);

                    var restorePackagesTask = kernel.RestoreAsync();
                    var delay = 500;
                    while (await Task.WhenAny(Task.Delay(delay), restorePackagesTask) != restorePackagesTask)
                    {
                        if (context.CancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        installMessage.Progress++;
                        CreateOrUpdateDisplayValue(context, installPackagesPropertyName, installMessage);
                    }

                    var result = await restorePackagesTask;

                    var resultMessage = new InstallPackagesMessage(
                        requestedSources,
                        Array.Empty<string>(),
                        kernel.ResolvedPackageReferences
                            .Where(r => requestedPackages.Contains(r.PackageName, StringComparer.OrdinalIgnoreCase))
                            .Select(s => $"{s.PackageName}, {s.PackageVersion}")
                            .OrderBy(s => s)
                            .ToList(),
                        0);

                    if (result.Succeeded)
                    {
                        kernel.RegisterResolvedPackageReferences(result.ResolvedReferences);
                        foreach (var resolvedReference in result.ResolvedReferences)
                        {
                            context.Publish(new PackageAdded(resolvedReference, context.Command));
                        }

                        CreateOrUpdateDisplayValue(context, installPackagesPropertyName, resultMessage);
                    }
                    else
                    {
                        var errors = string.Join(Environment.NewLine, result.Errors);
                        CreateOrUpdateDisplayValue(context, installPackagesPropertyName, resultMessage);
                        context.Fail(context.Command, message: errors);
                    }
                }

                await invocationContext.ScheduleAsync(Restore);
            };
        }
    }
}
