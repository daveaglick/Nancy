using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Internal;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Nancy.Configuration;
using Nancy.Responses;

namespace Nancy.ViewEngines.Razor
{
    using System.Linq;
    using System.Text;
    using System.Text.Encodings.Web;
    using Microsoft.AspNetCore.Mvc.Razor.Compilation;
    using Microsoft.AspNetCore.Mvc.Razor.Internal;
    using Microsoft.Extensions.FileProviders;
    using Microsoft.Extensions.Primitives;

    /// <summary>
    /// View engine for rendering razor views.
    /// </summary>
    public class RazorViewEngine : IViewEngine
    {
        public static readonly string[] SupportedExtensions = { "cshtml" };

        private readonly IRazorConfiguration razorConfiguration;
        private readonly IAssemblyCatalog assemblyCatalog;
        private readonly IViewLocationProvider locationProvider;
        private IViewLocator viewLocator;
        private IServiceProvider razorServices;

        /// <summary>
        /// Gets the extensions file extensions that are supported by the view engine.
        /// </summary>
        /// <value>An <see cref="IEnumerable{T}"/> instance containing the extensions.</value>
        /// <remarks>The extensions should not have a leading dot in the name.</remarks>
        public IEnumerable<string> Extensions
        {
            get { return SupportedExtensions; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RazorViewEngine"/> class.
        /// </summary>
        public RazorViewEngine(IRazorConfiguration razorConfiguration, IAssemblyCatalog assemblyCatalog, IViewLocationProvider locationProvider)
        {
            this.razorConfiguration = razorConfiguration;
            this.assemblyCatalog = assemblyCatalog;
            this.locationProvider = locationProvider;
        }

        public void Initialize(ViewEngineStartupContext viewEngineStartupContext)
        {
            viewLocator = viewEngineStartupContext.ViewLocator;

            IServiceCollection serviceCollection = new ServiceCollection();
            
            IMvcCoreBuilder mvcBuilder = serviceCollection
                .AddMvcCore()
                .AddRazorViewEngine();
            mvcBuilder.PartManager.FeatureProviders.Add(new MetadataReferenceFeatureProvider(this.razorConfiguration, this.assemblyCatalog));
            serviceCollection.Configure<RazorViewEngineOptions>(options =>
            {
                options.ViewLocationExpanders.Add(new ViewLocationExpander());
            });

            serviceCollection
                .AddSingleton<ILoggerFactory, SilentLoggerFactory>()
                .AddSingleton<DiagnosticSource, SilentDiagnosticSource>()
                .AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>()
                .AddSingleton<IHostingEnvironment>(new HostingEnvironment(locationProvider))
                .AddSingleton(razorConfiguration)
                .AddScoped<IMvcRazorHost, NancyRazorHost>();

            this.razorServices = serviceCollection.BuildServiceProvider();
        }

        public Response RenderView(ViewLocationResult viewLocation, dynamic model, IRenderContext renderContext)
        {
            var scopeFactory = razorServices.GetRequiredService<IServiceScopeFactory>();
            using (var scope = scopeFactory.CreateScope())
            {
                var response = new HtmlResponse
                {
                    Contents = stream =>
                    {
                        var viewEngine = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
                        var pageActivator = scope.ServiceProvider.GetRequiredService<IRazorPageActivator>();
                        var htmlEncoder = scope.ServiceProvider.GetRequiredService<HtmlEncoder>();
                        var pageFactoryProvider = scope.ServiceProvider.GetRequiredService<IRazorPageFactoryProvider>();
                        var razorCompilationService = scope.ServiceProvider.GetRequiredService<IRazorCompilationService>();
                        var view = this.GetViewFromViewLocation(viewLocation, viewEngine, pageActivator, 
                            htmlEncoder, pageFactoryProvider, razorCompilationService);
                        var output = new StreamWriter(stream);
                        var viewContext = GetViewContext(view, model, output, renderContext);
                        try
                        {
                            view.RenderAsync(viewContext).GetAwaiter().GetResult();
                        }
                        catch (InvalidOperationException ex)
                        {
                            // Throw a specialized exception for partial view location problems
                            if (ex.Message.StartsWith("The partial view"))
                            {
                                throw new ViewNotFoundException(ex.Message);
                            }
                            throw;
                        }
                        output.Flush();
                    }
                };
                return response;
            }
        }

        private ViewContext GetViewContext(IView view, dynamic model, TextWriter output, IRenderContext renderContext)
        {
            HttpContext httpContext = new DefaultHttpContext
            {
                RequestServices = razorServices
            };
            ActionContext actionContext = new ActionContext(
                httpContext, new RouteData(), new ActionDescriptor());
            ViewDataDictionary viewData = new ViewDataDictionary(
                new EmptyModelMetadataProvider(), actionContext.ModelState)
            {
                Model = model
            };
            ITempDataDictionary tempData = new TempDataDictionary(
                actionContext.HttpContext, razorServices.GetRequiredService<ITempDataProvider>());
            NancyViewContext viewContext = new NancyViewContext(renderContext, 
                actionContext, view, viewData, tempData, output, new HtmlHelperOptions());
            return viewContext;
        }

        /// <summary>
        /// Gets the view for a located view location.
        /// </summary>
        private IView GetViewFromViewLocation(ViewLocationResult viewLocation,
            IRazorViewEngine viewEngine, IRazorPageActivator pageActivator, HtmlEncoder htmlEncoder,
            IRazorPageFactoryProvider pageFactoryProvider, IRazorCompilationService razorCompilationService)
        {
            var viewStartLocations = ViewHierarchyUtility.GetViewStartLocations(viewLocation.Location);
            List<IRazorPage> viewStartPages = viewStartLocations
                .Select(pageFactoryProvider.CreateFactory)
                .Where(x => x.Success)
                .Select(x => x.RazorPageFactory())
                .Reverse()
                .ToList();
            IRazorPage page = GetPageFromViewLocation(viewLocation, razorCompilationService);
            return new RazorView(viewEngine, pageActivator, viewStartPages, page, htmlEncoder);
        }

        /// <summary>
        /// Gets the Razor page for a view location. This is roughly modeled on
        /// DefaultRazorPageFactory and CompilerCache.
        /// </summary>
        private IRazorPage GetPageFromViewLocation(ViewLocationResult viewLocation, IRazorCompilationService razorCompilationService)
        {
            // Get the file info by combining the view location with info found at the document's original location (if any)
            string relativePath = $"/{viewLocation.Location}/{viewLocation.Name}";
            IFileInfo fileInfo = new ViewLocationFileInfo(viewLocation);
            RelativeFileInfo relativeFileInfo = new RelativeFileInfo(fileInfo, relativePath);

            // Create the compilation
            CompilationResult compilationResult = razorCompilationService.Compile(relativeFileInfo);
            compilationResult.EnsureSuccessful();

            // Create and return the page
            // We're not actually using the cache, but the CompilerCacheResult ctor contains the logic to create the page factory
            CompilerCacheResult compilerCacheResult = new CompilerCacheResult(relativePath, compilationResult, new IChangeToken[] {});
            return compilerCacheResult.PageFactory();
        }
    }
}
