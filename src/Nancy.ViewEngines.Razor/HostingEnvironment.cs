using System;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Nancy.ViewEngines.Razor
{
    public  class HostingEnvironment : IHostingEnvironment
    {
        private readonly IFileProvider fileProvider;

        public HostingEnvironment(IViewLocationProvider locationProvider)
        {
            this.fileProvider = new FileProvider(locationProvider);
        }

        public string EnvironmentName
        {
            get { return "Nancy"; }
            set { throw new NotSupportedException(); }
        }

        // This gets used to load dependencies and is passed to Assembly.Load()
        public string ApplicationName
        {
            get { return typeof(HostingEnvironment).GetTypeInfo().Assembly.FullName; }
            set { throw new NotSupportedException(); }
        }

        public IFileProvider WebRootFileProvider
        {
            get { return fileProvider; }
            set { throw new NotSupportedException(); }
        }

        public string WebRootPath
        {
            get { return "/"; }
            set { throw new NotSupportedException(); }
        }

        public string ContentRootPath
        {
            get { return WebRootPath; }
            set { WebRootPath = value; }
        }

        public IFileProvider ContentRootFileProvider
        {
            get { return WebRootFileProvider; }
            set { WebRootFileProvider = value; }
        }
    }
}