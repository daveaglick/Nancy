namespace Nancy.ViewEngines.Razor
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Encodings.Web;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Abstractions;
    using Microsoft.AspNetCore.Mvc.ModelBinding;
    using Microsoft.AspNetCore.Mvc.Razor;
    using Microsoft.AspNetCore.Mvc.Razor.Compilation;
    using Microsoft.AspNetCore.Mvc.Razor.Internal;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using Microsoft.AspNetCore.Mvc.ViewEngines;
    using Microsoft.AspNetCore.Mvc.ViewFeatures;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Primitives;
    using Nancy.ViewEngines;
    using Nancy.ViewEngines.Razor;

    public class RazorViewRenderer
    {
        private readonly IServiceProvider serviceProvider;

        private readonly IRazorViewEngine viewEngine;

        private readonly IRazorPageActivator pageActivator;

        private readonly HtmlEncoder htmlEncoder;

        private readonly IRazorPageFactoryProvider pageFactoryProvider;

        private readonly IRazorCompilationService razorCompilationService;

        private readonly ITempDataProvider tempDataProvider;

        public RazorViewRenderer(IServiceProvider serviceProvider, IRazorViewEngine viewEngine, 
            IRazorPageActivator pageActivator,
            HtmlEncoder htmlEncoder, IRazorPageFactoryProvider pageFactoryProvider,
            IRazorCompilationService razorCompilationService,
            ITempDataProvider tempDataProvider)
        {
            this.serviceProvider = serviceProvider;
            this.viewEngine = viewEngine;
            this.pageActivator = pageActivator;
            this.htmlEncoder = htmlEncoder;
            this.pageFactoryProvider = pageFactoryProvider;
            this.razorCompilationService = razorCompilationService;
            this.tempDataProvider = tempDataProvider;
        }

        public void RenderView(ViewLocationResult viewLocation, dynamic model, IRenderContext renderContext, Stream stream)
        {
            // Get and render the view
            var view = this.GetViewFromViewLocation(viewLocation);
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

        private ViewContext GetViewContext(IView view, dynamic model, TextWriter output, IRenderContext renderContext)
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = this.serviceProvider
            };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), actionContext.ModelState)
            {
                Model = model
            };
            var tempData = new TempDataDictionary(actionContext.HttpContext, this.tempDataProvider);
            var viewContext = new NancyViewContext(renderContext,
                actionContext, view, viewData, tempData, output, new HtmlHelperOptions());
            return viewContext;
        }

        /// <summary>
        /// Gets the view for a located view location.
        /// </summary>
        private IView GetViewFromViewLocation(ViewLocationResult viewLocation)
        {
            var viewStartLocations = ViewHierarchyUtility.GetViewStartLocations(viewLocation.Location);
            var viewStartPages = viewStartLocations
                .Select(this.pageFactoryProvider.CreateFactory)
                .Where(x => x.Success)
                .Select(x => x.RazorPageFactory())
                .Reverse()
                .ToList();
            var page = GetPageFromViewLocation(viewLocation);
            return new RazorView(this.viewEngine, this.pageActivator, viewStartPages, page, this.htmlEncoder);
        }

        /// <summary>
        /// Gets the Razor page for a view location. This is roughly modeled on
        /// DefaultRazorPageFactory and CompilerCache.
        /// </summary>
        private IRazorPage GetPageFromViewLocation(ViewLocationResult viewLocation)
        {
            // Get the file info by combining the view location with info found at the document's original location (if any)
            string relativePath = $"/{viewLocation.Location}/{viewLocation.Name}";
            var fileInfo = new ViewLocationFileInfo(viewLocation);
            var relativeFileInfo = new RelativeFileInfo(fileInfo, relativePath);

            // Create the compilation
            var compilationResult = this.razorCompilationService.Compile(relativeFileInfo);
            compilationResult.EnsureSuccessful();

            // Create and return the page
            // We're not actually using the cache, but the CompilerCacheResult ctor contains the logic to create the page factory
            var compilerCacheResult = new CompilerCacheResult(relativePath, compilationResult, new IChangeToken[] { });
            return compilerCacheResult.PageFactory();
        }
    }
}