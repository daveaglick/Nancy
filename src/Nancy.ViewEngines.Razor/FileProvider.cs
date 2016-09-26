using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Nancy.ViewEngines.Razor
{
    public class FileProvider : IFileProvider
    {
        private readonly IViewLocationProvider locationProvider;

        public FileProvider(IViewLocationProvider locationProvider)
        {
            this.locationProvider = locationProvider;
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            string[] segments = subpath.Split(new [] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 1)
            {
                return new ViewLocationFileInfo(null);
            }
            return new ViewLocationFileInfo(locationProvider.GetLocatedViews(
                RazorViewEngine.SupportedExtensions, 
                (segments.Length == 1 ? string.Empty : string.Join("/", segments.Take(segments.Length - 1))),
                Path.GetFileNameWithoutExtension(segments[segments.Length - 1]))
                .FirstOrDefault());
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            return new DirectoryContents(locationProvider, subpath);
        }

        public IChangeToken Watch(string filter)
        {
            return new EmptyChangeToken();
        }

        private class DirectoryContents : IDirectoryContents
        {
            private readonly IViewLocationProvider locationProvider;
            private string subpath;

            public DirectoryContents(IViewLocationProvider locationProvider, string subpath)
            {
                this.locationProvider = locationProvider;
                this.subpath = subpath;
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                return locationProvider
                    .GetLocatedViews(RazorViewEngine.SupportedExtensions)
                    .Where(viewLocation => viewLocation.Location == subpath)
                    .Select(viewLocation => new ViewLocationFileInfo(viewLocation))
                    .GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public bool Exists
            {
                get { return this.Any(); }
            }
        }
    }
}