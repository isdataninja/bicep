// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.IO;
using System.IO.Pipelines;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Bicep.Core.Emit;
using Bicep.Core.FileSystem;
using Bicep.Core.Registry;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Az;
using Bicep.Core.Workspaces;
using Bicep.LanguageServer.CompilationManager;
using Bicep.LanguageServer.Completions;
using Bicep.LanguageServer.Handlers;
using Bicep.LanguageServer.Providers;
using Bicep.LanguageServer.Registry;
using Bicep.LanguageServer.Snippets;
using Bicep.LanguageServer.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using OmnisharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Bicep.LanguageServer.Utils;

namespace Bicep.LanguageServer
{
    public class Server
    {
        public class CreationOptions
        {
            public ISnippetsProvider? SnippetsProvider { get; set; }

            public IResourceTypeProvider? ResourceTypeProvider { get; set; }

            public IFileResolver? FileResolver { get; set; }
        }

        private readonly OmnisharpLanguageServer server;

        public Server(PipeReader input, PipeWriter output, CreationOptions creationOptions)
            : this(creationOptions, options => options.WithInput(input).WithOutput(output))
        {
        }

        public Server(Stream input, Stream output, CreationOptions creationOptions)
            : this(creationOptions, options => options.WithInput(input).WithOutput(output))
        {
        }
        
        private Server(CreationOptions creationOptions, Action<LanguageServerOptions> onOptionsFunc)
        {
            BicepDeploymentsInterop.Initialize();
            server = OmnisharpLanguageServer.PreInit(options =>
            {
                options
                    .WithHandler<BicepTextDocumentSyncHandler>()
                    .WithHandler<BicepDocumentSymbolHandler>()
                    .WithHandler<BicepDefinitionHandler>()
                    .WithHandler<BicepDeploymentGraphHandler>()
                    .WithHandler<BicepReferencesHandler>()
                    .WithHandler<BicepDocumentHighlightHandler>()
                    .WithHandler<BicepDocumentFormattingHandler>()
                    .WithHandler<BicepRenameHandler>()
                    .WithHandler<BicepHoverHandler>()
                    .WithHandler<BicepCompletionHandler>()
                    .WithHandler<BicepCodeActionHandler>()
                    .WithHandler<BicepDidChangeWatchedFilesHandler>()
                    .WithHandler<BicepSignatureHelpHandler>()
                    .WithHandler<BicepSemanticTokensHandler>()
                    .WithHandler<BicepTelemetryHandler>()
                    .WithHandler<BicepBuildCommandHandler>()
                    .WithServices(services => RegisterServices(creationOptions, services));

                onOptionsFunc(options);
            });
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            await server.Initialize(cancellationToken);

            server.LogInfo($"Running on processId {Environment.ProcessId}");

            if (bool.TryParse(Environment.GetEnvironmentVariable("BICEP_TRACING_ENABLED"), out var enableTracing) && enableTracing)
            {
                Trace.Listeners.Add(new ServerLogTraceListener(server));
            }

            var scheduler = server.GetService<IModuleRestoreScheduler>();
            scheduler.Start();

            await server.WaitForExit;
        }

        private static void RegisterServices(CreationOptions creationOptions, IServiceCollection services)
        {
            var fileResolver = creationOptions.FileResolver ?? new FileResolver();
            // using type based registration so dependencies can be injected automatically
            // without manually constructing up the graph
            services.AddSingleton<IResourceTypeProvider>(services => creationOptions.ResourceTypeProvider ?? AzResourceTypeProvider.CreateWithAzTypes());
            services.AddSingleton<ISnippetsProvider>(services => creationOptions.SnippetsProvider ?? new SnippetsProvider(fileResolver));
            services.AddSingleton<IFileResolver>(services => fileResolver);
            services.AddSingleton<IModuleRegistryProvider, DefaultModuleRegistryProvider>();
            services.AddSingleton<IModuleDispatcher, ModuleDispatcher>();
            services.AddSingleton<ITelemetryProvider, TelemetryProvider>();
            services.AddSingleton<IWorkspace, Workspace>();
            services.AddSingleton<ICompilationManager, BicepCompilationManager>();
            services.AddSingleton<ICompilationProvider, BicepCompilationProvider>();
            services.AddSingleton<ISymbolResolver, BicepSymbolResolver>();
            services.AddSingleton<ICompletionProvider, BicepCompletionProvider>();
            services.AddSingleton<IModuleRestoreScheduler, ModuleRestoreScheduler>();
        }
    }
}
