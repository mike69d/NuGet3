﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGet.Packaging
{
    /// <summary>
    /// Writes the packages.config XML file to a stream
    /// </summary>
    public class PackagesConfigWriter : IDisposable
    {
        private readonly Stream _stream;
        private bool _disposed;
        private bool _closed;
        private readonly List<PackageReference> _entries;
        private NuGetVersion _minClientVersion;
        private IFrameworkNameProvider _frameworkMappings;
        private XDocument _xDocument;

        /// <summary>
        /// Create a packages.config writer
        /// </summary>
        /// <param name="stream">Stream to write the XML packages.config file into</param>
        public PackagesConfigWriter(Stream stream)
            : this(DefaultFrameworkNameProvider.Instance, stream)
        {
        }

        public PackagesConfigWriter(IFrameworkNameProvider frameworkMappings, Stream stream)
        {
            _stream = stream;
            _closed = false;
            _entries = new List<PackageReference>();
            _frameworkMappings = frameworkMappings;
            _xDocument = XDocument.Load(_stream, LoadOptions.PreserveWhitespace);
        }

        /// <summary>
        /// Write a minimum client version to packages.config
        /// </summary>
        /// <param name="version">Minumum version of the client required to parse and use this file.</param>
        public void WriteMinClientVersion(NuGetVersion version)
        {
            if (_minClientVersion != null)
            {
                throw new PackagingException(String.Format(CultureInfo.InvariantCulture, "MinClientVersion already exists"));
            }

            _minClientVersion = version;
        }

        /// <summary>
        /// Add a package entry
        /// </summary>
        /// <param name="packageId">Package Id</param>
        /// <param name="version">Package Version</param>
        public void WritePackageEntry(string packageId, NuGetVersion version, NuGetFramework targetFramework)
        {
            if (packageId == null)
            {
                throw new ArgumentNullException("packageId");
            }

            if (version == null)
            {
                throw new ArgumentNullException("version");
            }

            if (targetFramework == null)
            {
                throw new ArgumentNullException("targetFramework");
            }

            WritePackageEntry(new PackageIdentity(packageId, version), targetFramework);
        }

        /// <summary>
        /// Adds a basic package entry to the file
        /// </summary>
        public void WritePackageEntry(PackageIdentity identity, NuGetFramework targetFramework)
        {
            var entry = new PackageReference(identity, targetFramework);

            WritePackageEntry(entry);
        }

        /// <summary>
        /// Adds a package entry to the file
        /// </summary>
        /// <param name="entry">Package reference entry</param>
        public void WritePackageEntry(PackageReference entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException("entry");
            }

            if (_disposed || _closed)
            {
                throw new PackagingException("Writer closed. Unable to add entry.");
            }

            if (_entries.Where(e => StringComparer.OrdinalIgnoreCase.Equals(e.PackageIdentity.Id, entry.PackageIdentity.Id)).Any())
            {
                throw new PackagingException(String.Format(CultureInfo.InvariantCulture, "Package entry already exists. Id: {0}", entry.PackageIdentity.Id));
            }

            _entries.Add(entry);

        }

        /// <summary>
        /// Adds a package entry to the file
        /// </summary>
        /// <param name="entry">Package reference entry</param>
        public void UpdatePackageEntry(PackageReference entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException("entry");
            }

            if (_disposed || _closed)
            {
                throw new PackagingException("Writer closed. Unable to add entry.");
            }

            if (_entries.Where(e => StringComparer.OrdinalIgnoreCase.Equals(e.PackageIdentity.Id, entry.PackageIdentity.Id)).Any())
            {
                throw new PackagingException(String.Format(CultureInfo.InvariantCulture, "Package entry already exists. Id: {0}", entry.PackageIdentity.Id));
            }

            // Check if package entry already exist on packages.config file
            var matchingEntry = _xDocument.Descendants("package")
                .Where(e => e.FirstAttribute.Value.Equals(entry.PackageIdentity.Id, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            var newEntry = CreateXElementForPackageEntry(entry);

            if (matchingEntry != null)
            {
                _xDocument.ReplaceWith(matchingEntry, newEntry);
            }
        }

        private void WriteFile()
        {
            var packages = new XElement(XName.Get("packages"));

            if (_minClientVersion != null)
            {
                var minClientVersionAttribute = new XAttribute(XName.Get("minClientVersion"), _minClientVersion.ToNormalizedString());
                packages.Add(minClientVersionAttribute);
            }

            _xDocument.Add(packages);

            var sorted = _entries.OrderBy(e => e.PackageIdentity.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in sorted)
            {
                var node = CreateXElementForPackageEntry(entry);
                packages.Add(node);
            }

            _xDocument.Save(_stream);
        }

        private XElement CreateXElementForPackageEntry(PackageReference entry)
        {
            var node = new XElement(XName.Get("package"));

            node.Add(new XAttribute(XName.Get("id"), entry.PackageIdentity.Id));
            node.Add(new XAttribute(XName.Get("version"), entry.PackageIdentity.Version));

            // map the framework to the short name
            // special frameworks such as any and unsupported will be ignored here
            if (entry.TargetFramework.IsSpecificFramework)
            {
                var frameworkShortName = entry.TargetFramework.GetShortFolderName(_frameworkMappings);

                if (!String.IsNullOrEmpty(frameworkShortName))
                {
                    node.Add(new XAttribute(XName.Get("targetFramework"), frameworkShortName));
                }
            }

            if (entry.HasAllowedVersions)
            {
                node.Add(new XAttribute(XName.Get("allowedVersions"), entry.AllowedVersions.ToString()));
            }

            if (entry.IsDevelopmentDependency)
            {
                node.Add(new XAttribute(XName.Get("developmentDependency"), "true"));
            }

            if (entry.RequireReinstallation)
            {
                node.Add(new XAttribute(XName.Get("requireReinstallation"), "true"));
            }

            return node;
        }

        /// <summary>
        /// Write the file to the stream and close it to disallow further changes.
        /// </summary>
        public void Close()
        {
            if (!_closed)
            {
                _closed = true;

                WriteFile();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_closed)
            {
                Close();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Dispose(true);
            }
        }
    }
}
