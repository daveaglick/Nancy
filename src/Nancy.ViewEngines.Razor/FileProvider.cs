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
                return null;
            }
            return new FileInfo(locationProvider.GetLocatedViews(
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
                    .Select(viewLocation => new FileInfo(viewLocation))
                    .GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public bool Exists => this.Any();
        }

        private class FileInfo : IFileInfo
        {
            private readonly ViewLocationResult viewLocation;

            public FileInfo(ViewLocationResult viewLocation)
            {
                this.viewLocation = viewLocation;
            }

            public bool Exists
            {
                get { return viewLocation != null; }
            }

            public long Length
            {
                get { return viewLocation?.Contents()?.ReadToEnd()?.Length ?? 0; }
            }

            public string PhysicalPath
            {
                get { return viewLocation?.Location ?? null; }
            }

            public string Name
            {
                get { return viewLocation?.Name ?? null; }
            }

            public DateTimeOffset LastModified
            {
                get { return DateTimeOffset.Now; }
            }

            public bool IsDirectory
            {
                get { return false; }
            }

            public Stream CreateReadStream()
            {
                var reader = viewLocation?.Contents();
                if (reader == null)
                {
                    return null;
                }
                if (((StreamReader) reader).BaseStream != null)
                {
                    return ((StreamReader) reader).BaseStream;
                }
                return new TextReaderStream(reader);
            }
        }
    }
}