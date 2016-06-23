using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Web.Hosting;
using System.Web.WebPages.Html;
using Geta.Web.StaticFileHasher.Cryptography;

namespace Geta.Web.StaticFileHasher.Helpers
{
    public static class FileHashHelper
    {
        private static readonly char[] _relativePathIndicators = { '~', '/', '.' };

        private static readonly IEqualityComparer<string> _comparer = StringComparer.InvariantCultureIgnoreCase;
        private static readonly IDictionary<string, string> _files = new Dictionary<string, string>(_comparer);
        private static readonly IDictionary<string, object> _locks = new Dictionary<string, object>(_comparer);
        private static readonly IDictionary<string, FileSystemWatcher> _watchers = new Dictionary<string, FileSystemWatcher>(_comparer);

        private static readonly uint[] _table = Crc32.InitializeTable(Crc32.DefaultPolynomial);
        private static readonly uint _seed = Crc32.DefaultSeed;
        private static readonly object _masterLock = new object();

        public static string GetHash(string file)
        {
            file = ResolvePath(file);
            if (string.IsNullOrEmpty(file)) return file;

            AppendLock(file);

            lock (_locks[file])
            {
                if (_files.ContainsKey(file)) return _files[file];

                var hash = ComputeHash(file);
                if (string.IsNullOrEmpty(hash)) return file;

                _files.Add(file, hash);

                var folder = Path.GetDirectoryName(file);
                if (!_watchers.ContainsKey(folder))
                {
                    _watchers.Add(folder, CreateWatcher(folder));
                }

                return hash;
            }
        }

        private static void AppendLock(string file)
        {
            lock (_masterLock)
            {
                if (!_locks.ContainsKey(file))
                    _locks.Add(file, new object());
            }
        }

        public static IHtmlString AppendHash(this HtmlHelper helper, string url)
        {
            var hash = GetHash(url);
            if (string.IsNullOrEmpty(hash)) return new HtmlString(url);

            url = url.TrimStart('~');

            var index = url.IndexOf('?');
            var symbol = index > -1 ? "&" : "?";

            return new HtmlString(url + symbol + hash);
        }

        private static FileSystemWatcher CreateWatcher(string folder)
        {
            var watcher = new FileSystemWatcher(folder)
            {
                Path = folder,
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnFileChange;
            watcher.Renamed += OnFileRenamed;
            watcher.Deleted += OnFileDeleted;

            return watcher;
        }

        private static void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (_files.ContainsKey(e.FullPath)) _files.Remove(e.FullPath);
        }

        private static void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (_files.ContainsKey(e.OldFullPath)) _files.Remove(e.OldFullPath);
        }

        private static void OnFileChange(object sender, FileSystemEventArgs e)
        {
            if (_files.ContainsKey(e.FullPath)) _files.Remove(e.FullPath);
        }

        private static string ComputeHash(string file)
        {
            uint hash;

            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    hash = Crc32.Compute(_table, _seed, stream);
                }
            }
            catch (FileLoadException)
            {
                return null;
            }

            return HttpServerUtility.UrlTokenEncode(BitConverter.GetBytes(hash));
        }

        private static string ResolvePath(string file)
        {
            if (file.IndexOfAny(_relativePathIndicators) == 0)
                file = HostingEnvironment.MapPath(file);

            return !File.Exists(file) ? string.Empty : file;
        }
    }
}